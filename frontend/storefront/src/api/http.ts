export type ProblemDetails = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
  problems?: string[];
  traceId?: string;
  [extension: string]: unknown;
};

export type HttpErrorKind = "http" | "network" | "invalid-response";

export class HttpError extends Error {
  readonly kind: HttpErrorKind;
  readonly status: number | null;
  readonly problem: ProblemDetails | null;
  readonly traceId: string | null;
  readonly retryAfter: string | null;

  constructor(
    kind: HttpErrorKind,
    status: number | null,
    problem: ProblemDetails | null = null,
    traceId: string | null = null,
    retryAfter: string | null = null,
  ) {
    super(
      kind === "network"
        ? "The request could not reach the server."
        : `The request failed${status === null ? "" : ` with status ${status}`}.`,
    );
    this.name = "HttpError";
    this.kind = kind;
    this.status = status;
    this.problem = problem;
    this.traceId = traceId;
    this.retryAfter = retryAfter;
  }
}

type RequestOptions = {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
  notifyUnauthorized?: boolean;
};

export type Download = { blob: Blob; filename: string };

type UnauthorizedListener = () => void;
const unauthorizedListeners = new Set<UnauthorizedListener>();

export function onUnauthorized(listener: UnauthorizedListener) {
  unauthorizedListeners.add(listener);
  return () => {
    unauthorizedListeners.delete(listener);
  };
}

export function isHttpError(error: unknown): error is HttpError {
  return error instanceof HttpError;
}

export function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}

export async function requestJson<T>(
  url: string,
  options: RequestOptions = {},
): Promise<T> {
  const response = await send(url, options);
  if (response.status === 204 || response.status === 205) return undefined as T;

  const text = await response.text();
  if (text.length === 0) return undefined as T;
  if (!isJson(response.headers.get("Content-Type"))) {
    throw invalidResponse(response);
  }

  try {
    return JSON.parse(text) as T;
  } catch {
    throw invalidResponse(response);
  }
}

export async function requestBlob(
  url: string,
  fallbackFilename: string,
  options: Omit<RequestOptions, "body"> & { expectedContentType?: string } = {},
): Promise<Download> {
  const response = await send(url, options);
  const expected = options.expectedContentType ?? "application/pdf";
  const contentType =
    response.headers.get("Content-Type")?.split(";")[0].trim().toLowerCase() ??
    "";
  if (contentType !== expected.toLowerCase()) throw invalidResponse(response);

  return {
    blob: await response.blob(),
    filename: safeFilename(
      response.headers.get("Content-Disposition"),
      fallbackFilename,
    ),
  };
}

export function saveDownload({ blob, filename }: Download) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.hidden = true;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
}

async function send(url: string, options: RequestOptions) {
  if (!url.startsWith("/"))
    throw new TypeError("HTTP requests must use a same-origin path.");

  const formBody =
    typeof FormData !== "undefined" && options.body instanceof FormData
      ? options.body
      : null;
  const body =
    options.body === undefined
      ? undefined
      : (formBody ?? JSON.stringify(options.body));
  let response: Response;
  try {
    response = await fetch(url, {
      method: options.method ?? "GET",
      signal: options.signal,
      headers:
        options.body === undefined || formBody
          ? undefined
          : { "Content-Type": "application/json" },
      body,
    });
  } catch (error) {
    if (isAbortError(error)) throw error;
    throw new HttpError("network", null);
  }

  if (!response.ok) {
    const error = await responseError(response);
    if (response.status === 401 && options.notifyUnauthorized !== false) {
      for (const listener of unauthorizedListeners) listener();
    }
    throw error;
  }

  return response;
}

async function responseError(response: Response) {
  let problem: ProblemDetails | null = null;
  if (isJson(response.headers.get("Content-Type"))) {
    try {
      const value = (await response.json()) as unknown;
      if (value && typeof value === "object") problem = value as ProblemDetails;
    } catch {
      problem = null;
    }
  }

  const bodyTrace =
    typeof problem?.traceId === "string" ? problem.traceId : null;
  return new HttpError(
    "http",
    response.status,
    problem,
    bodyTrace ?? response.headers.get("X-Trace-Id"),
    response.headers.get("Retry-After"),
  );
}

function invalidResponse(response: Response) {
  return new HttpError(
    "invalid-response",
    response.status,
    null,
    response.headers.get("X-Trace-Id"),
  );
}

function isJson(contentType: string | null) {
  return contentType?.toLowerCase().includes("json") ?? false;
}

function safeFilename(disposition: string | null, fallback: string) {
  let supplied: string | null = null;
  const encoded = disposition?.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  const plain = disposition?.match(/filename="?([^";]+)"?/i)?.[1];
  try {
    supplied = encoded ? decodeURIComponent(encoded) : (plain?.trim() ?? null);
  } catch {
    supplied = null;
  }

  const leaf = supplied
    ?.split(/[\\/]/)
    .pop()
    ?.split("")
    .filter(
      (character) =>
        character.charCodeAt(0) >= 32 && character.charCodeAt(0) !== 127,
    )
    .join("")
    .trim();
  return leaf && leaf !== "." && leaf !== ".." ? leaf : fallback;
}
