"use client";

/**
 * The catalogue's shape, and the rules about what may be removed from it.
 *
 * The tree is shown as a tree because it is one: a flat list of parents and children is where an
 * administrator deletes a category that has products in it. Indentation carries the relationship,
 * the product count is stated on every row so the consequence is visible *before* the button is
 * pressed, and a category that holds products or children offers no delete at all rather than
 * offering one and explaining afterwards.
 *
 * The API decides what may be deleted: a category with products or children is refused with a 422
 * and a sentence. That sentence is shown as-is, because it is more specific than anything this file
 * could invent — "move or remove the child categories first" is advice, not an error code.
 */

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, Plus } from "lucide-react";

import { ConfirmDialog } from "@/components/shared/ConfirmDialog";
import { TextAreaField, TextField } from "@/components/forms/FormField";
import { EmptyState, ErrorState, StatusBadge } from "@/components/shared/Feedback";
import { adminApi } from "@/features/admin/api/adminApi";
import { errorMessage } from "@/lib/errors";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { AdminCategory } from "@/types/admin";

interface CategoryFormValues {
  name: string;
  slug: string;
  description: string;
  imageUrl: string;
  parentId: string;
  displayOrder: string;
  isActive: boolean;
}

export function AdminCategories() {
  const queryClient = useQueryClient();
  const { push } = useToast();

  const [editing, setEditing] = useState<AdminCategory | "new" | null>(null);
  const [deleting, setDeleting] = useState<AdminCategory | null>(null);
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const [actionError, setActionError] = useState<string | null>(null);

  // Inactive categories are invisible to the default read, so they are always asked for: an admin
  // managing the catalogue needs to see the ones they switched off.
  const categories = useQuery({ queryKey: queryKeys.admin.categories(), queryFn: () => adminApi.categories() });

  const save = useMutation({
    mutationFn: (input: { id?: string; values: CategoryFormValues }) => {
      if (input.id) {
        return adminApi.updateCategory(input.id, {
          name: input.values.name.trim(),
          // A blank slug keeps the existing address on update, and asks the API to generate one on
          // create. Sending whitespace would be a slug change to nothing.
          slug: input.values.slug.trim() || null,
          description: input.values.description.trim() || null,
          imageUrl: input.values.imageUrl.trim() || null,
          displayOrder: Number(input.values.displayOrder) || 0,
          // Always sent: the API's update DTO takes a non-nullable flag, so omitting it would
          // deactivate the category instead of leaving it alone.
          isActive: input.values.isActive,
        });
      }

      return adminApi.createCategory({
        name: input.values.name.trim(),
        slug: input.values.slug.trim() || null,
        description: input.values.description.trim() || null,
        imageUrl: input.values.imageUrl.trim() || null,
        parentId: input.values.parentId || null,
        displayOrder: Number(input.values.displayOrder) || 0,
      });
    },
    onSuccess: async (_result, input) => {
      setEditing(null);
      setActionError(null);
      // The category tree, and the catalogue the categories shape.
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.categories() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.categories.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.products.all }),
      ]);

      push({
        tone: "success",
        title: input.id ? "Category saved" : "Category created",
        body: input.values.isActive === false && input.id ? "It is now hidden from shoppers." : undefined,
      });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  const remove = useMutation({
    mutationFn: (id: string) => adminApi.deleteCategory(id),
    onSuccess: async () => {
      const name = deleting?.name;
      setDeleting(null);
      setActionError(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.categories() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.categories.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.products.all }),
      ]);
      push({ tone: "success", title: `${name} deleted`, body: "It is no longer part of the catalogue." });
    },
    onError: error => {
      setDeleting(null);
      setActionError(errorMessage(error));
    },
  });

  const toggleActive = useMutation({
    mutationFn: (input: { category: AdminCategory; isActive: boolean }) =>
      adminApi.updateCategory(input.category.id, {
        name: input.category.name,
        slug: input.category.slug,
        description: input.category.description,
        imageUrl: input.category.imageUrl,
        displayOrder: input.category.displayOrder,
        isActive: input.isActive,
      }),
    onSuccess: async (_result, input) => {
      setActionError(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.admin.categories() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.categories.all }),
        queryClient.invalidateQueries({ queryKey: queryKeys.products.all }),
      ]);
      push({
        tone: "success",
        title: `${input.category.name} is now ${input.isActive ? "visible" : "hidden"}`,
      });
    },
    onError: error => setActionError(errorMessage(error)),
  });

  if (categories.isPending) {
    return <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} aria-hidden />;
  }

  if (categories.isError || !categories.data) {
    return <ErrorState message="We could not load the categories." onRetry={() => void categories.refetch()} />;
  }

  const roots = categories.data;
  const flat = flatten(roots);

  return (
    <div className="mp-stack">
      <div className="mp-spread">
        <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          {roots.length} {roots.length === 1 ? "top-level category" : "top-level categories"},{" "}
          {flat.length} in total.
        </p>

        <button type="button" className="btn btn-sm btn-primary" onClick={() => setEditing("new")}>
          <Plus size={14} aria-hidden className="me-1" />
          New category
        </button>
      </div>

      {actionError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {actionError}
        </p>
      ) : null}

      {roots.length === 0 ? (
        <EmptyState
          title="No categories yet"
          body="Nothing can be listed without a category, so this is the first thing to build."
          action={
            <button type="button" className="btn btn-sm btn-primary" onClick={() => setEditing("new")}>
              Create the first category
            </button>
          }
        />
      ) : (
        <div className="mp-card" style={{ padding: "var(--space-2) var(--space-4)" }}>
          <CategoryRows
            nodes={roots}
            depth={0}
            collapsed={collapsed}
            onToggle={id => setCollapsed(current => ({ ...current, [id]: !current[id] }))}
            onEdit={category => setEditing(category)}
            onDelete={category => setDeleting(category)}
            onToggleActive={category => toggleActive.mutate({ category, isActive: !category.isActive })}
            busy={toggleActive.isPending || remove.isPending}
          />
        </div>
      )}

      {editing ? (
        <CategoryForm
          category={editing === "new" ? null : editing}
          parents={flat}
          busy={save.isPending}
          error={actionError}
          onCancel={() => {
            setEditing(null);
            setActionError(null);
          }}
          onSubmit={values => save.mutate({ id: editing === "new" ? undefined : editing.id, values })}
        />
      ) : null}

      {deleting ? (
        <ConfirmDialog
          show
          title={`Delete ${deleting.name}?`}
          confirmLabel={`Delete ${deleting.name}`}
          cancelLabel="Keep it"
          busy={remove.isPending}
          onCancel={() => setDeleting(null)}
          onConfirm={() => remove.mutate(deleting.id)}
        >
          <p className="mb-3">
            <strong>{deleting.name}</strong> disappears from the catalogue and from the category filters. Nothing in a
            listing is rewritten.
          </p>
          <p className="mb-0">
            If any product is filed under it, or it has child categories, the API refuses this and the category stays
            where it is. Deactivating it instead hides it from shoppers and keeps the products intact.
          </p>
        </ConfirmDialog>
      ) : null}
    </div>
  );
}

/**
 * The tree, one row per category.
 *
 * Recursion rather than a flat table with an indent column: a child is only ever rendered inside
 * its parent, which is the relationship the API models, and it means a collapsed branch takes its
 * whole subtree with it.
 */
function CategoryRows({
  nodes,
  depth,
  collapsed,
  onToggle,
  onEdit,
  onDelete,
  onToggleActive,
  busy,
}: {
  nodes: AdminCategory[];
  depth: number;
  collapsed: Record<string, boolean>;
  onToggle: (id: string) => void;
  onEdit: (category: AdminCategory) => void;
  onDelete: (category: AdminCategory) => void;
  onToggleActive: (category: AdminCategory) => void;
  busy: boolean;
}) {
  return (
    <ul className="list-unstyled mb-0">
      {nodes.map(node => {
        const hasChildren = node.children.length > 0;
        const isCollapsed = collapsed[node.id] === true;
        // Deleting is only offered where the API will accept it: no children and no products.
        const canDelete = !hasChildren && node.productCount === 0;

        return (
          <li key={node.id} style={{ borderTop: depth === 0 ? "1px solid var(--border)" : "none" }}>
            <div
              className="d-flex align-items-center flex-wrap"
              style={{ gap: "var(--space-2)", padding: "var(--space-2) 0", paddingLeft: `${depth * 1.25}rem` }}
            >
              {hasChildren ? (
                <button
                  type="button"
                  className="btn btn-sm"
                  style={{ padding: 0, color: "var(--text-subtle)" }}
                  aria-expanded={!isCollapsed}
                  onClick={() => onToggle(node.id)}
                  aria-label={isCollapsed ? `Show the categories under ${node.name}` : `Hide the categories under ${node.name}`}
                >
                  {isCollapsed ? <ChevronRight size={14} aria-hidden /> : <ChevronDown size={14} aria-hidden />}
                </button>
              ) : (
                <span aria-hidden style={{ width: "1rem" }} />
              )}

              <span style={{ flex: 1, minWidth: 0 }}>
                <span style={{ fontWeight: depth === 0 ? 600 : 400 }}>{node.name}</span>
                <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                  /{node.slug} · {node.productCount} {node.productCount === 1 ? "product" : "products"}
                </span>
              </span>

              <StatusBadge tone={node.isActive ? "success" : "info"}>{node.isActive ? "Visible" : "Hidden"}</StatusBadge>

              <div className="d-flex" style={{ gap: "0.35rem", flex: "none" }}>
                <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => onToggleActive(node)} disabled={busy}>
                  {node.isActive ? "Hide" : "Show"}
                </button>
                <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => onEdit(node)} disabled={busy}>
                  Edit
                </button>
                {canDelete ? (
                  <button
                    type="button"
                    className="btn btn-sm btn-outline-danger"
                    onClick={() => onDelete(node)}
                    disabled={busy}
                    aria-label={`Delete ${node.name}`}
                  >
                    Delete
                  </button>
                ) : null}
              </div>
            </div>

            {hasChildren && !isCollapsed ? (
              <CategoryRows
                nodes={node.children}
                depth={depth + 1}
                collapsed={collapsed}
                onToggle={onToggle}
                onEdit={onEdit}
                onDelete={onDelete}
                onToggleActive={onToggleActive}
                busy={busy}
              />
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}

function CategoryForm({
  category,
  parents,
  busy,
  error,
  onCancel,
  onSubmit,
}: {
  category: AdminCategory | null;
  parents: AdminCategory[];
  busy: boolean;
  error: string | null;
  onCancel: () => void;
  onSubmit: (values: CategoryFormValues) => void;
}) {
  const [values, setValues] = useState<CategoryFormValues>({
    name: category?.name ?? "",
    slug: category?.slug ?? "",
    description: category?.description ?? "",
    imageUrl: category?.imageUrl ?? "",
    parentId: category?.parentId ?? "",
    displayOrder: String(category?.displayOrder ?? 0),
    isActive: category?.isActive ?? true,
  });

  const [fieldError, setFieldError] = useState<Record<string, string>>({});
  const isEdit = Boolean(category);

  function set<K extends keyof CategoryFormValues>(key: K, value: CategoryFormValues[K]) {
    setValues(current => ({ ...current, [key]: value }));
  }

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setFieldError({});

    // The two rules the API enforces that are worth catching before a round trip: a name is
    // required, and a slug must already be the shape the API's pattern accepts — lower case,
    // because validation runs before the service lower-cases anything.
    const errors: Record<string, string> = {};

    if (values.name.trim().length === 0) {
      errors.name = "Name the category.";
    }

    if (values.slug.trim().length > 0 && !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(values.slug.trim())) {
      errors.slug = "Lower-case letters, digits and single dashes, e.g. home-office.";
    }

    if (Object.keys(errors).length > 0) {
      setFieldError(errors);
      return;
    }

    onSubmit(values);
  }

  return (
    <form onSubmit={submit} noValidate className="mp-card mp-stack" style={{ padding: "var(--space-4)" }} aria-labelledby="category-form">
      <h2 className="mp-section-title" id="category-form" style={{ margin: 0, fontSize: "var(--fs-h3)" }}>
        {isEdit ? `Edit ${category!.name}` : "A new category"}
      </h2>

      {error ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {error}
        </p>
      ) : null}

      <div className="row g-2">
        <div className="col-12 col-md-6">
          <TextField
            label="Name"
            required
            maxLength={120}
            value={values.name}
            placeholder="Home & Office"
            error={fieldError.name}
            onChange={event => set("name", event.target.value)}
          />
        </div>

        <div className="col-12 col-md-6">
          <TextField
            label="Address"
            maxLength={160}
            value={values.slug}
            placeholder="home-office"
            hint={
              isEdit
                ? "The part after /categories/. Leave blank to keep the current address."
                : "Optional. Leave blank and the API builds one from the name."
            }
            error={fieldError.slug}
            onChange={event => set("slug", event.target.value)}
          />
        </div>
      </div>

      <div className="row g-2">
        <div className="col-12 col-md-6">
          <label htmlFor="category-parent" className="mp-metric-label">
            Sits under
          </label>
          <select
            id="category-parent"
            className="form-select form-control-sm"
            value={values.parentId}
            disabled={isEdit}
            onChange={event => set("parentId", event.target.value)}
          >
            <option value="">A top-level category</option>
            {parents
              .filter(option => option.id !== category?.id)
              .map(option => (
                <option key={option.id} value={option.id}>
                  {option.name}
                </option>
              ))}
          </select>
          <p style={{ margin: "0.25rem 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
            {isEdit
              ? "The API has no way to move an existing category, so this is fixed once created."
              : "Nesting is one level at a time; a child can have children of its own."}
          </p>
        </div>

        <div className="col-6 col-md-3">
          <TextField
            label="Order"
            type="number"
            min="0"
            step="1"
            inputMode="numeric"
            value={values.displayOrder}
            hint="Lower shows first."
            onChange={event => set("displayOrder", event.target.value)}
          />
        </div>

        <div className="col-12 col-md-3">
          <TextField
            label="Image address"
            type="url"
            value={values.imageUrl}
            placeholder="https://…"
            onChange={event => set("imageUrl", event.target.value)}
          />
        </div>
      </div>

      <TextAreaField
        label="Description"
        rows={2}
        maxLength={2000}
        value={values.description}
        onChange={event => set("description", event.target.value)}
      />

      {isEdit ? (
        <label className="d-flex align-items-center" style={{ gap: "0.5rem", fontSize: "var(--fs-sm)" }}>
          <input
            type="checkbox"
            checked={values.isActive}
            onChange={event => set("isActive", event.target.checked)}
          />
          Shoppers can see this category
        </label>
      ) : null}

      <div className="d-flex" style={{ gap: "0.5rem" }}>
        <button type="submit" className="btn btn-sm btn-primary" disabled={busy}>
          {busy ? "Saving…" : isEdit ? "Save category" : "Create category"}
        </button>
        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
      </div>
    </form>
  );
}

function flatten(nodes: AdminCategory[]): AdminCategory[] {
  return nodes.flatMap(node => [node, ...flatten(node.children)]);
}