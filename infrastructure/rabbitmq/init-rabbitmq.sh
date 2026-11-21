#!/bin/sh
set -eu

RABBIT_API="http://rabbitmq:15672/api"
AUTH_USER="rabbitmq_admin"
AUTH_PASS="${RABBITMQ_BOOTSTRAP_PASSWORD:?RABBITMQ_BOOTSTRAP_PASSWORD must be set}"

echo "Waiting for RabbitMQ Management API..."
curl -fsS --retry 15 --retry-delay 2 --retry-all-errors \
  -u "${AUTH_USER}:${AUTH_PASS}" \
  "${RABBIT_API}/overview" > /dev/null

api_put() {
  path="$1"
  body="$2"
  curl -fsS -X PUT \
    -u "${AUTH_USER}:${AUTH_PASS}" \
    -H "Content-Type: application/json" \
    -d "${body}" \
    "${RABBIT_API}${path}" > /dev/null
}

echo "Creating vhost 'marketplace'..."
api_put "/vhosts/marketplace" '{}'

echo "Granting bootstrap admin access to vhost 'marketplace'..."
api_put "/permissions/marketplace/${AUTH_USER}" '{"configure":".*","write":".*","read":".*"}'

create_service_user() {
  svc_user="$1"
  svc_pass="$2"
  echo "Provisioning RabbitMQ user '${svc_user}'..."
  api_put "/users/${svc_user}" "{\"password\":\"${svc_pass}\",\"tags\":\"\"}"
  api_put "/permissions/marketplace/${svc_user}" '{"configure":".*","write":".*","read":".*"}'
}

create_service_user "wallet_service" "${RABBITMQ_WALLET_PASSWORD:?RABBITMQ_WALLET_PASSWORD must be set}"
create_service_user "inventory_service" "${RABBITMQ_INVENTORY_PASSWORD:?RABBITMQ_INVENTORY_PASSWORD must be set}"
create_service_user "marketplace_service" "${RABBITMQ_MARKETPLACE_PASSWORD:?RABBITMQ_MARKETPLACE_PASSWORD must be set}"
create_service_user "admin_service" "${RABBITMQ_ADMIN_PASSWORD:?RABBITMQ_ADMIN_PASSWORD must be set}"


declare_queue() {
  queue_name="$1"
  echo "Declaring durable queue '${queue_name}'..."
  api_put "/queues/marketplace/${queue_name}" '{"durable":true,"auto_delete":false,"arguments":{}}'
}

for q in \
  wallet-commands \
  inventory-commands \
  marketplace-saga \
  marketplace-projection \
  admin-analytics
do
  declare_queue "${q}"
  declare_queue "${q}_error"
done
echo "RabbitMQ bootstrap completed."
