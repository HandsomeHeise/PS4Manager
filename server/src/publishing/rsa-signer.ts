import {
  constants,
  createHash,
  createPrivateKey,
  createPublicKey,
  sign,
  verify,
  type KeyObject
} from "node:crypto";
export class RsaSigner {
  private readonly key: KeyObject;
  readonly keyId: string;
  constructor(pem: string | Buffer) {
    this.key = createPrivateKey(pem);
    if (
      this.key.asymmetricKeyType !== "rsa" ||
      (this.key.asymmetricKeyDetails?.modulusLength ?? 0) < 2048
    )
      throw new Error("An RSA key of at least 2048 bits is required");
    this.keyId = createHash("sha256")
      .update(createPublicKey(this.key).export({ type: "spki", format: "der" }))
      .digest("hex")
      .slice(0, 16);
  }
  sign(payload: Buffer): Promise<{ keyId: string; signatureBase64: string }> {
    return Promise.resolve({
      keyId: this.keyId,
      signatureBase64: sign("RSA-SHA256", payload, {
        key: this.key,
        padding: constants.RSA_PKCS1_PADDING
      }).toString("base64")
    });
  }
  verify(payload: Buffer, signatureBase64: string, keyId: string): boolean {
    return (
      keyId === this.keyId &&
      verify(
        "RSA-SHA256",
        payload,
        { key: createPublicKey(this.key), padding: constants.RSA_PKCS1_PADDING },
        Buffer.from(signatureBase64, "base64")
      )
    );
  }
}
