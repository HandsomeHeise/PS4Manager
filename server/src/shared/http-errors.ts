export class HttpError extends Error {
  constructor(
    readonly statusCode: number,
    readonly code: string
  ) {
    super(code);
  }
}
export function badRequest(): never {
  throw new HttpError(400, "invalid_request");
}
export function notFound(): never {
  throw new HttpError(404, "not_found");
}
