FROM node:24-bookworm-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY tsconfig.json ./
COPY src ./src
COPY scripts ./scripts
RUN npm run build && npm prune --omit=dev
FROM rclone/rclone:1.70 AS rclone
FROM mysql:8.4
USER root
RUN microdnf install -y tar gzip findutils && microdnf clean all
COPY --from=build /usr/local/bin/node /usr/local/bin/node
COPY --from=build /app/node_modules /app/node_modules
COPY --from=build /app/dist /app/dist
COPY --from=rclone /usr/local/bin/rclone /usr/local/bin/rclone
COPY migrations /app/migrations
COPY package.json /app/package.json
COPY backup /app/backup
RUN sed -i 's/\r$//' /app/backup/*.sh && chmod 755 /app/backup/*.sh
ENTRYPOINT ["/app/backup/run.sh"]
