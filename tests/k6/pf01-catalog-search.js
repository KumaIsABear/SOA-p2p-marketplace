import http from "k6/http";
import { check } from "k6";
import { Counter, Trend } from "k6/metrics";

const pf01Latency = new Trend("pf01_catalog_ms", true);
const pf01Errors = new Counter("pf01_errors");

export const options = {
  scenarios: {
    pf01_sequential: {
      executor: "shared-iterations",
      vus: 1,
      iterations: 200,
      maxDuration: "2m",
    },
  },
  thresholds: {
    pf01_catalog_ms: ["avg<=100"],
    pf01_errors: ["count==0"],
  },
};

const baseUrl = __ENV.BASE_URL || "http://gateway:8080";
const email = __ENV.BUYER_EMAIL || "buyer@marketplace.local";
const password = __ENV.AUTH_SEED_PASSWORD || "";
const searchUrl = `${baseUrl}/api/v1/marketplace/listings?category=Weapon&rarity=Epic&sort=priceAsc&page=1&pageSize=20`;

export function setup() {
  const loginRes = http.post(
    `${baseUrl}/api/v1/auth/login`,
    JSON.stringify({ email, password }),
    { headers: { "Content-Type": "application/json" } },
  );

  if (loginRes.status !== 200) {
    throw new Error(`Login failed: ${loginRes.status} ${loginRes.body}`);
  }

  const token = loginRes.json("accessToken");
  const params = { headers: { Authorization: `Bearer ${token}` } };

  for (let i = 0; i < 20; i++) {
    const warmRes = http.get(searchUrl, params);
    if (warmRes.status !== 200) {
      throw new Error(
        `Warmup #${i + 1} failed: ${warmRes.status} ${warmRes.body}`,
      );
    }
  }

  return { token };
}

export default function (data) {
  const res = http.get(searchUrl, {
    headers: { Authorization: `Bearer ${data.token}` },
  });

  const ok = check(res, {
    "status is 200": (r) => r.status === 200,
    "returns 20 items": (r) =>
      r.status === 200 && r.json("items").length === 20,
  });

  if (ok) {
    pf01Latency.add(res.timings.duration);
  } else {
    pf01Errors.add(1);
  }
}
