/** The public directory of stores. */

import { apiClient } from "@/api/axiosClient";
import type { PagedResult } from "@/types/api";
import type { StoreDirectoryEntry } from "@/types/store";

export type { StoreDirectoryEntry };

export type StoreDirectoryPage = PagedResult<StoreDirectoryEntry>;

export const storeDirectoryApi = {
  /**
   * Every active storefront, best rated first.
   *
   * Anonymous, and filtered on the server: a directory of twenty shops with a search box is a
   * query the server answers, not twenty cards the browser filters after they arrive.
   */
  async list(params: { page?: number; pageSize?: number; search?: string } = {}): Promise<StoreDirectoryPage> {
    const search = new URLSearchParams({ page: String(params.page ?? 1), pageSize: String(params.pageSize ?? 24) });

    if (params.search) {
      search.set("search", params.search);
    }

    const { data } = await apiClient.get<StoreDirectoryPage>(`/api/stores?${search.toString()}`);
    return data;
  },
};
