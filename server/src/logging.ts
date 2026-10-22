import type { FastifyLoggerOptions, PinoLoggerOptions } from "fastify/types/logger.js";
import type { AppConfig } from "./config.js";

const sensitiveKey = /(?:password|passphrase|secret|token|totp|csrf|authorization|cookie)/i;

function secretValues(config: AppConfig): string[] {
  return [
    config.database.passwordFile,
    config.sessionSecretFile,
    config.totpEncryptionKeyFile,
    config.modPrivateKeyFile,
    config.updatePublicKeyFile
  ];
}

function stripQueryString(value: string): string {
  const queryStart = value.indexOf("?");
  return queryStart === -1 ? value : value.slice(0, queryStart);
}

function sanitizeString(value: string, secrets: readonly string[]): string {
  let sanitized = value;
  for (const secret of secrets) {
    sanitized = sanitized.replaceAll(secret, "[Redacted]");
  }
  return sanitized;
}

function sanitize(
  value: unknown,
  secrets: readonly string[],
  seen: WeakSet<object>,
  propertyName?: string
): unknown {
  if (propertyName === "body" || (propertyName !== undefined && sensitiveKey.test(propertyName))) {
    return undefined;
  }
  if (typeof value === "string") {
    if (propertyName === "msg" && !["incoming request", "request completed"].includes(value))
      return "[Redacted]";
    const sanitized = sanitizeString(value, secrets);
    return propertyName === "url" ? stripQueryString(sanitized) : sanitized;
  }
  if (value === null || typeof value !== "object") {
    return value;
  }
  if (seen.has(value)) {
    return "[Circular]";
  }
  seen.add(value);

  if (Array.isArray(value)) {
    return value
      .map((item) => sanitize(item, secrets, seen))
      .filter((item): item is Exclude<typeof item, undefined> => item !== undefined);
  }

  const result: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(value)) {
    const sanitized = sanitize(item, secrets, seen, key);
    if (sanitized !== undefined) {
      result[key] = sanitized;
    }
  }
  return result;
}

export function serializeLogRecord(record: unknown, config: AppConfig): string {
  return JSON.stringify(sanitize(record, secretValues(config), new WeakSet<object>()));
}

export function createLoggerOptions(
  config: AppConfig,
  destination: { write(value: string): unknown } = process.stdout
): FastifyLoggerOptions & PinoLoggerOptions {
  return {
    // Apply privacy filtering to the final serialized log, after Fastify/Pino
    // have added messages and bindings. No unsanitized fallback on parse errors.
    stream: {
      write(value: string) {
        try {
          destination.write(serializeLogRecord(JSON.parse(value), config) + "\n");
        } catch {
          destination.write('{"level":50,"msg":"Log serialization failed"}\n');
        }
      }
    },
    level: config.environment === "test" ? "silent" : "info",
    redact: {
      paths: [
        "req.headers.authorization",
        "req.headers.cookie",
        "res.headers.set-cookie",
        "body",
        "password",
        "totp",
        "csrf",
        "*.password",
        "*.totp",
        "*.csrf"
      ],
      censor: "[Redacted]"
    },
    serializers: {
      req(request) {
        const route = request.routeOptions.url;
        return {
          method: request.method,
          url: stripQueryString(request.url),
          remoteAddress: request.ip,
          ...(route === undefined ? {} : { route })
        };
      },
      res(response) {
        return { statusCode: response.statusCode };
      },
      err(error) {
        return {
          type: error.name,
          message: "[Redacted]",
          stack: "[Redacted]",
          statusCode: error.statusCode
        };
      }
    }
  };
}
