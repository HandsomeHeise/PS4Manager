import { createCipheriv, createDecipheriv, randomBytes } from "node:crypto";
export class SecretBox {
  private readonly key: Buffer;
  constructor(key: Buffer) {
    if (key.length !== 32) throw new Error("TOTP encryption key must be 32 bytes");
    this.key = Buffer.from(key);
  }
  seal(value: Buffer): Buffer {
    const nonce = randomBytes(12),
      cipher = createCipheriv("aes-256-gcm", this.key, nonce);
    cipher.setAAD(Buffer.from("PS4MANAGER-TOTP-V1"));
    const bytes = Buffer.concat([cipher.update(value), cipher.final()]);
    return Buffer.concat([Buffer.from([1]), nonce, cipher.getAuthTag(), bytes]);
  }
  open(value: Buffer): Buffer {
    if (value.length < 29 || value[0] !== 1) throw new Error("Invalid encrypted secret");
    const cipher = createDecipheriv("aes-256-gcm", this.key, value.subarray(1, 13));
    cipher.setAAD(Buffer.from("PS4MANAGER-TOTP-V1"));
    cipher.setAuthTag(value.subarray(13, 29));
    return Buffer.concat([cipher.update(value.subarray(29)), cipher.final()]);
  }
}
