import hashlib, hmac, json, time, urllib.parse, urllib.request
from http.server import BaseHTTPRequestHandler, HTTPServer

KEY2 = "test-key2"
CALLBACK_TARGET = "http://localhost:8080/api/v1/wallet/zalopay/callback"
ORDERS = {}

def hmac_hex(key: str, data: str) -> str:
    return hmac.new(key.encode("utf-8"), data.encode("utf-8"), hashlib.sha256).hexdigest()

def read_params(handler: BaseHTTPRequestHandler) -> dict:
    length = int(handler.headers.get("Content-Length", "0"))
    raw = handler.rfile.read(length).decode("utf-8") if length > 0 else ""
    ctype = (handler.headers.get("Content-Type") or "").lower()
    if "application/json" in ctype:
        return json.loads(raw) if raw else {}
    return {k: v[0] for k, v in urllib.parse.parse_qs(raw).items()}

class MockZaloPayHandler(BaseHTTPRequestHandler):
    def _send_json(self, payload: dict, status: int = 200):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _send_html(self, html: str, status: int = 200):
        body = html.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        params = read_params(self)

        if self.path.rstrip("/") == "/v2/create":
            trans_id = str(params["app_trans_id"])
            ORDERS[trans_id] = {
                "app_id": int(params["app_id"]),
                "app_trans_id": trans_id,
                "app_time": int(params["app_time"]),
                "app_user": str(params["app_user"]),
                "amount": int(params["amount"]),
                "embed_data": str(params.get("embed_data", "{}")),
                "item": str(params.get("item", "[]")),
                "zp_trans_id": int(time.time() * 1000),
                "paid": False,
            }
            self._send_json({
                "return_code": 1,
                "return_message": "Success",
                "sub_return_code": 1,
                "sub_return_message": "",
                "zp_trans_token": f"mock-token-{trans_id}",
                "order_url": f"http://localhost:9999/pay/{trans_id}",
                "order_token": f"mock-token-{trans_id}",
            })
            return

        if self.path.rstrip("/") == "/v2/query":
            trans_id = str(params.get("app_trans_id", ""))
            order = ORDERS.get(trans_id)
            if not order:
                self._send_json({"return_code": 2, "return_message": "Not found", "sub_return_code": -49, "is_processing": False})
                return
            if order["paid"]:
                self._send_json({
                    "return_code": 1,
                    "return_message": "Success",
                    "sub_return_code": 1,
                    "is_processing": False,
                    "amount": order["amount"],
                    "zp_trans_id": order["zp_trans_id"],
                })
            else:
                self._send_json({
                    "return_code": 3,
                    "return_message": "Pending",
                    "sub_return_code": 0,
                    "is_processing": True,
                    "amount": order["amount"],
                    "zp_trans_id": 0,
                })
            return

        if self.path.startswith("/confirm/"):
            trans_id = self.path.split("/confirm/", 1)[1]
            order = ORDERS.get(trans_id)
            if not order:
                self._send_html("<h3>Không tìm thấy đơn</h3>", 404)
                return

            order["paid"] = True
            cb_data = json.dumps({
                "app_id": order["app_id"],
                "app_trans_id": order["app_trans_id"],
                "app_time": order["app_time"],
                "app_user": order["app_user"],
                "amount": order["amount"],
                "embed_data": order["embed_data"],
                "item": order["item"],
                "zp_trans_id": order["zp_trans_id"],
                "server_time": int(time.time() * 1000),
                "channel": 38,
                "merchant_user_id": order["app_user"],
            }, separators=(",", ":"))
            cb_body = json.dumps({
                "data": cb_data,
                "mac": hmac_hex(KEY2, cb_data),
                "type": 1,
            }).encode("utf-8")

            req = urllib.request.Request(
                CALLBACK_TARGET,
                data=cb_body,
                headers={"Content-Type": "application/json"},
                method="POST",
            )
            with urllib.request.urlopen(req, timeout=10) as resp:
                resp_text = resp.read().decode("utf-8")

            self._send_html(f"""
                <body style="font-family:sans-serif;background:#0b0f17;color:#f1f5f9;padding:32px;">
                  <h2>Thanh toán giả lập thành công!</h2>
                  <p>AppTransId: <code>{trans_id}</code></p>
                  <p>Callback response từ Gateway/Wallet.API: <code>{resp_text}</code></p>
                  <p><a style="color:#38bdf8" href="http://localhost:8080/?topUpReturn=1">← Quay lại P2P Marketplace</a></p>
                </body>
            """)
            return

        self._send_json({"error": "not found"}, 404)

    def do_GET(self):
        if self.path.startswith("/pay/"):
            trans_id = self.path.split("/pay/", 1)[1]
            order = ORDERS.get(trans_id)
            if not order:
                self._send_html("<h3>Không tìm thấy đơn thanh toán</h3>", 404)
                return
            self._send_html(f"""
                <body style="font-family:sans-serif;background:#0b0f17;color:#f1f5f9;padding:32px;">
                  <h2>ZaloPay Local Mock Checkout</h2>
                  <p>AppTransId: <code>{trans_id}</code></p>
                  <p>Số tiền: <strong>{order['amount']:,} VND</strong></p>
                  <form method="POST" action="/confirm/{trans_id}">
                    <button type="submit" style="padding:10px 18px;font-size:15px;background:#22c55e;color:#fff;border:none;border-radius:6px;cursor:pointer;">
                      ✓ Xác nhận thanh toán (Gửi Callback HMAC Key2)
                    </button>
                  </form>
                </body>
            """)
            return
        self._send_html("<h3>ZaloPay Mock đang chạy tại :9999</h3>")

if __name__ == "__main__":
    print("ZaloPay Mock listening on http://0.0.0.0:9999")
    HTTPServer(("0.0.0.0", 9999), MockZaloPayHandler).serve_forever()