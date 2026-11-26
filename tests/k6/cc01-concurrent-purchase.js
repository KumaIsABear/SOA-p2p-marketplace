import http from "k6/http";
import exec from "k6/execution";
import { check, sleep } from "k6";
import { Counter } from "k6/metrics";

const buyerCount = Number(__ENV.BUYER_COUNT || 100);
const baseUrl = __ENV.BASE_URL || "http://gateway:8080";
const listingId = __ENV.LISTING_ID || "";
const password = __ENV.AUTH_SEED_PASSWORD || "";

const accepted202 = new Counter("cc01_accepted_202");
const conflict409 = new Counter("cc01_conflict_409");
const unexpectedStatus = new Counter("cc01_unexpected_status");
const completedTrades = new Counter("cc01_completed_trades");
const failedTrades = new Counter("cc01_failed_trades");

http.setResponseCallback(http.expectedStatuses(200, 202, 409));

export const options = {
  setupTimeout: "3m",
  scenarios: {
    cc01_race: {
      executor: "per-vu-iterations",
      vus: buyerCount,
      iterations: 1,
      maxDuration: "2m",
    },
  },
  thresholds: {
    cc01_accepted_202: ["count==1"],
    cc01_conflict_409: [`count==${buyerCount - 1}`],
    cc01_unexpected_status: ["count==0"],
    cc01_completed_trades: ["count==1"],
    cc01_failed_trades: ["count==0"],
  },
};

function uuidv4() {
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export function setup() {
  if (!listingId) {
    throw new Error("LISTING_ID environment variable is required.");
  }

  const tokens = new Array(buyerCount);
  const batchSize = 10;

  for (let start = 0; start < buyerCount; start += batchSize) {
    const reqs = [];
    const end = Math.min(start + batchSize, buyerCount);

    for (let i = start; i < end; i++) {
      const num = String(i + 1).padStart(3, "0");
      reqs.push([
        "POST",
        `${baseUrl}/api/v1/auth/login`,
        JSON.stringify({
          email: `cc01.buyer.${num}@marketplace.local`,
          password,
        }),
        { headers: { "Content-Type": "application/json" } },
      ]);
    }

    const responses = http.batch(reqs);
    for (let j = 0; j < responses.length; j++) {
      const res = responses[j];
      if (res.status !== 200) {
        throw new Error(
          `Buyer #${start + j + 1} login failed: ${res.status} ${res.body}`,
        );
      }
      tokens[start + j] = res.json("accessToken");
    }
  }

  return {
    tokens,
    launchAtMs: Date.now() + 1500,
  };
}

export default function (data) {
  const idx = exec.vu.idInTest - 1;
  const token = data.tokens[idx];

  const waitSec = (data.launchAtMs - Date.now()) / 1000;
  if (waitSec > 0) {
    sleep(waitSec);
  }

  const res = http.post(
    `${baseUrl}/api/v1/marketplace/listings/${listingId}/purchase`,
    null,
    {
      headers: {
        Authorization: `Bearer ${token}`,
        "Idempotency-Key": uuidv4(),
      },
    },
  );

  if (res.status === 409) {
    conflict409.add(1);
    return;
  }

  if (res.status !== 202) {
    unexpectedStatus.add(1);
    console.error(`VU ${idx + 1} unexpected status ${res.status}: ${res.body}`);
    return;
  }

  accepted202.add(1);
  const tradeId = res.json("tradeId");
  const statusUrl =
    res.json("statusUrl") || `/api/v1/marketplace/trades/${tradeId}`;

  let finalStatus = "Pending";
  for (let attempt = 0; attempt < 120; attempt++) {
    sleep(0.25);
    const poll = http.get(`${baseUrl}${statusUrl}`, {
      headers: { Authorization: `Bearer ${token}` },
    });

    if (poll.status === 200) {
      finalStatus = poll.json("status");
      if (
        finalStatus === "Completed" ||
        finalStatus === "Failed" ||
        finalStatus === "RequiresReview"
      ) {
        break;
      }
    }
  }

  const ok = check(finalStatus, {
    "accepted trade reached Completed": (s) => s === "Completed",
  });

  if (ok) {
    completedTrades.add(1);
    console.log(
      `WINNER: buyer #${idx + 1} | tradeId=${tradeId} | status=${finalStatus}`,
    );
  } else {
    failedTrades.add(1);
    console.error(
      `ACCEPTED TRADE DID NOT COMPLETE: buyer #${idx + 1} | tradeId=${tradeId} | status=${finalStatus}`,
    );
  }
}
