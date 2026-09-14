export class GatewayError extends Error {
  constructor(status, code, message) {
    super(message);
    this.name = "GatewayError";
    this.status = status;
    this.code = code;
  }
}

export function errorEnvelope(error) {
  if (error instanceof GatewayError) {
    return {
      status: error.status,
      body: { error: { code: error.code, message: error.message } },
    };
  }

  return {
    status: 500,
    body: {
      error: {
        code: "internal_error",
        message: "The resident gateway could not complete the request.",
      },
    },
  };
}
