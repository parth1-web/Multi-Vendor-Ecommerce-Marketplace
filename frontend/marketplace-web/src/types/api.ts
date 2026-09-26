/** The problem+json shape the API returns for every failure. */

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  /** Field name to messages, as returned by the validation filter. */
  errors?: Record<string, string[] | string>;
  extensions?: Record<string, unknown>;
}

/** What a component actually consumes: one shape for every failure, whatever threw it. */
export interface ApiErrorShape {
  status: number;
  type?: string;
  title: string;
  detail: string;
  fieldErrors?: Record<string, string[]>;
  correlationId?: string;
}

/** Paged envelope used by every list endpoint. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}
