/**
 * The form behind a new listing and an edit.
 *
 * One component for both, because they are the same questions asked of the same data, and two
 * copies drift: the edit form ends up missing a rule the create form has, and a seller discovers
 * it the hard way when a save is refused.
 *
 * The shape of the form is a plain object held in state rather than a field-per-input form,
 * because most of it is a list: variants, images and specifications grow and shrink, and a
 * field-per-input form has no way to say "and one more of those".
 */

"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery } from "@tanstack/react-query";
import { Plus, Trash2 } from "lucide-react";
import { z } from "zod";

import { SelectField, TextAreaField, TextField } from "@/components/forms/FormField";
import { ErrorState } from "@/components/shared/Feedback";
import { categoryApi } from "@/features/products/api/productApi";
import { sellerApi } from "@/features/seller/api/sellerApi";
import { errorMessage, fieldErrors } from "@/lib/errors";
import { queryKeys } from "@/lib/queryKeys";
import type { Category, ProductImage, ProductSpecification, ProductVariant } from "@/types/product";
import type { CreateProductRequest, SellerProductDetail } from "@/types/productAuthoring";

const basicsSchema = z.object({
  name: z.string().trim().min(3, "Give the product a name shoppers will recognise.").max(200),
  shortDescription: z
    .string()
    .trim()
    .min(10, "One line that will appear in listings and search results.")
    .max(500),
  description: z
    .string()
    .trim()
    .min(30, "Describe the product: what it is, who it is for, what it does.")
    .max(20000),
  brand: z.string().trim().max(100).optional(),
  model: z.string().trim().max(100).optional(),
  basePrice: z.coerce.number().positive("Enter a price above zero."),
  compareAtPrice: z.coerce.number().positive("Enter a price above zero.").optional(),
  categoryId: z.string().uuid("Choose the category this belongs in."),
});

type BasicsValues = z.infer<typeof basicsSchema>;

interface VariantDraft {
  key: string;
  sku: string;
  name: string;
  price: string;
  initialStock: string;
  lowStockThreshold: string;
}

interface ImageDraft {
  key: string;
  url: string;
  altText: string;
  isPrimary: boolean;
}

interface SpecDraft {
  key: string;
  specKey: string;
  value: string;
}

export function ProductEditor({ productId }: { productId?: string }) {
  const categories = useQuery({ queryKey: queryKeys.categories.tree(), queryFn: () => categoryApi.tree() });

  const existing = useQuery({
    queryKey: queryKeys.seller.product(productId!),
    queryFn: () => sellerApi.product(productId!),
    enabled: Boolean(productId),
  });

  if (categories.isPending || (Boolean(productId) && existing.isPending)) {
    return <div className="mp-skeleton" style={{ height: "24rem", borderRadius: "var(--radius)" }} />;
  }

  if (categories.isError) {
    return <ErrorState message="We could not load the categories, so the form cannot be filled in yet." />;
  }

  if (productId && existing.isError) {
    return <ErrorState message="We could not open that listing, so there is nothing to edit." />;
  }

  // Keyed on the listing, so the form is built once with the values it is editing. An edit form
  // that starts blank is a form that overwrites: everything the seller did not retype would be
  // saved as empty, and they would find out when the listing came back wrong. The alternative,
  // filling the form in from an effect once the data arrives, renders the empty form first and
  // then rewrites it underneath the seller's hands.
  return <ListingForm key={productId ?? "new"} productId={productId} existing={existing.data ?? null} />;
}

function ListingForm({ productId, existing }: { productId?: string; existing: SellerProductDetail | null }) {
  const router = useRouter();
  const isEdit = Boolean(productId);

  const categories = useQuery({ queryKey: queryKeys.categories.tree(), queryFn: () => categoryApi.tree() });
  const categoryOptions = useMemo(() => flattenCategories(categories.data ?? []), [categories.data]);

  const [variants, setVariants] = useState<VariantDraft[]>(() => draftsFromVariants(existing?.variants));
  const [images, setImages] = useState<ImageDraft[]>(() => draftsFromImages(existing?.images));
  const [specifications, setSpecifications] = useState<SpecDraft[]>(() => draftsFromSpecifications(existing?.specifications));
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const {
    register,
    handleSubmit,
    getValues,
    setError,
    formState: { errors },
  } = useForm<BasicsValues>({
    resolver: zodResolver(basicsSchema),
    defaultValues: existing
      ? {
          name: existing.name,
          shortDescription: existing.shortDescription,
          description: existing.description,
          brand: existing.brand ?? "",
          model: existing.model ?? "",
          basePrice: existing.basePrice,
          compareAtPrice: existing.compareAtPrice ?? undefined,
          categoryId: existing.categoryId,
        }
      : { name: "", shortDescription: "", description: "", brand: "", model: "", basePrice: 0, categoryId: "" },
  });

  async function onSubmit(values: BasicsValues) {
    setFormError(null);
    setSubmitting(true);

    const request: CreateProductRequest = {
      name: values.name,
      shortDescription: values.shortDescription,
      description: values.description,
      categoryId: values.categoryId,
      basePrice: values.basePrice,
      compareAtPrice: values.compareAtPrice ? values.compareAtPrice : null,
      brand: values.brand?.trim() || null,
      model: values.model?.trim() || null,
      images: images
        .filter(image => image.url.trim().length > 0)
        .map((image, index) => ({ url: image.url.trim(), altText: image.altText.trim() || null, isPrimary: index === 0 })),
      variants: variants.map(variant => ({
        sku: variant.sku.trim(),
        name: variant.name.trim() || "Default",
        price: variant.price ? Number(variant.price) : null,
        initialStock: Number(variant.initialStock) || 0,
        lowStockThreshold: variant.lowStockThreshold ? Number(variant.lowStockThreshold) : null,
        options: [],
      })),
      specifications: specifications
        .filter(spec => spec.specKey.trim().length > 0)
        .map(spec => ({ key: spec.specKey.trim(), value: spec.value.trim() })),
      tags: [],
    };

    // A product with no image or no variant is refused by the API, and the reason is worth
    // saying here rather than as a validation error about a field the seller cannot see.
    if (request.images.length === 0) {
      setFormError("Add at least one image URL. A listing with no picture cannot be bought.");
      setSubmitting(false);
      return;
    }

    if (request.variants.some(variant => variant.sku.trim().length === 0)) {
      setFormError("Every variant needs a SKU, so orders can be matched to stock.");
      setSubmitting(false);
      return;
    }

    try {
      if (isEdit && productId) {
        // The API updates the product's own fields here; images, variants and specifications are
        // separate calls, because they are separate operations with their own rules.
        await sellerApi.updateProduct(productId, {
          name: request.name,
          shortDescription: request.shortDescription,
          description: request.description,
          categoryId: request.categoryId,
          basePrice: request.basePrice,
          compareAtPrice: request.compareAtPrice,
          brand: request.brand,
          model: request.model,
        });
      } else {
        await sellerApi.createProduct(request);
      }

      router.push("/seller/products");
    } catch (error) {
      for (const [field, message] of Object.entries(fieldErrors(error))) {
        const key = field.toLowerCase();
        if (key in getValues()) {
          setError(key as keyof BasicsValues, { message });
        }
      }

      setFormError(errorMessage(error));
      setSubmitting(false);
    }
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="mp-stack">
      {formError ? (
        <p role="alert" className="mp-alert mp-alert-danger">
          {formError}
        </p>
      ) : null}

      <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="editor-basics">
        <h2 className="mp-section-title" id="editor-basics" style={{ fontSize: "var(--fs-h3)" }}>
          What is it?
        </h2>

        <div className="mp-stack">
          <TextField label="Name" required error={errors.name?.message} {...register("name")} />

          <TextField
            label="One-line description"
            required
            hint="This is what shoppers read in a listing and in search results."
            error={errors.shortDescription?.message}
            {...register("shortDescription")}
          />

          <TextAreaField
            label="Full description"
            required
            hint="What it is, who it is for, and anything a buyer would ask before ordering."
            error={errors.description?.message}
            {...register("description")}
          />

          <div className="row g-2">
            <div className="col-12 col-sm-6">
              <SelectField label="Category" required error={errors.categoryId?.message} {...register("categoryId")}>
                <option value="">Choose a category…</option>
                {categoryOptions.map(option => (
                  <option key={option.id} value={option.id}>
                    {option.label}
                  </option>
                ))}
              </SelectField>
            </div>
            <div className="col-12 col-sm-3">
              <TextField label="Brand" error={errors.brand?.message} {...register("brand")} />
            </div>
            <div className="col-12 col-sm-3">
              <TextField label="Model" error={errors.model?.message} {...register("model")} />
            </div>
          </div>
        </div>
      </section>

      <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="editor-price">
        <h2 className="mp-section-title" id="editor-price" style={{ fontSize: "var(--fs-h3)" }}>
          What does it cost?
        </h2>

        <div className="row g-2">
          <div className="col-12 col-sm-6">
            <TextField
              label="Price"
              type="number"
              inputMode="decimal"
              step="0.01"
              min="0.01"
              required
              error={errors.basePrice?.message}
              {...register("basePrice")}
            />
          </div>
          <div className="col-12 col-sm-6">
            <TextField
              label="Was"
              type="number"
              inputMode="decimal"
              step="0.01"
              min="0.01"
              hint="Optional. Shown struck through, to make a discount visible."
              error={errors.compareAtPrice?.message}
              {...register("compareAtPrice")}
            />
          </div>
        </div>
      </section>

      <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="editor-images">
        <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
          <h2 className="mp-section-title" id="editor-images" style={{ fontSize: "var(--fs-h3)", margin: 0 }}>
            Images
          </h2>
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setImages(list => [...list, { key: nextKey(), url: "", altText: "", isPrimary: false }])}>
            <Plus size={14} aria-hidden className="me-1" />
            Add an image
          </button>
        </div>

        <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          Paste an image address. The first one is the picture shoppers see in listings.
        </p>

        <div className="mp-stack-sm">
          {images.map((image, index) => (
            <div key={image.key} className="row g-2 align-items-start">
              <div className="col-12 col-md-5">
                <TextField
                  label={index === 0 ? "Main image" : `Image ${index + 1}`}
                  type="url"
                  placeholder="https://…"
                  value={image.url}
                  onChange={event => setImages(list => list.map(item => (item.key === image.key ? { ...item, url: event.target.value } : item)))}
                />
              </div>
              <div className="col-12 col-md-5">
                <TextField
                  label="Alt text"
                  hint="What a screen reader should say. The product name is a good start."
                  value={image.altText}
                  onChange={event => setImages(list => list.map(item => (item.key === image.key ? { ...item, altText: event.target.value } : item)))}
                />
              </div>
              <div className="col-12 col-md-2">
                {images.length > 1 ? (
                  <button
                    type="button"
                    className="btn btn-sm"
                    onClick={() => setImages(list => list.filter(item => item.key !== image.key))}
                    aria-label={`Remove image ${index + 1}`}
                    style={{ color: "var(--text-subtle)" }}
                  >
                    <Trash2 size={16} aria-hidden />
                  </button>
                ) : null}
              </div>
            </div>
          ))}
        </div>
      </section>

      <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="editor-variants">
        <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
          <h2 className="mp-section-title" id="editor-variants" style={{ fontSize: "var(--fs-h3)", margin: 0 }}>
            What can a shopper buy?
          </h2>
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setVariants(list => [...list, newVariant()])}>
            <Plus size={14} aria-hidden className="me-1" />
            Add a variant
          </button>
        </div>

        <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          One row for a size, a colour or a length. A product with only one option needs exactly one row, with a
          descriptive name.
        </p>

        {isEdit ? (
          <p className="mp-alert mp-alert-info" style={{ marginBottom: 0 }}>
            These are shown so you can see what the listing has. Images, variants, stock and specifications are
            separate operations on the product page, where each one can be checked on its own.{" "}
            <Link href={`/seller/products/${productId}`} className="mp-link">
              Open the listing
            </Link>
            .
          </p>
        ) : null}

        <div className="mp-table-wrap">
          <table className="mp-table">
            <caption className="visually-hidden">Variants and their stock</caption>
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">SKU</th>
                <th scope="col">Price override</th>
                <th scope="col">Stock</th>
                <th scope="col">Warn below</th>
                <th scope="col" />
              </tr>
            </thead>
            <tbody>
              {variants.map((variant, index) => (
                <tr key={variant.key}>
                  <td>
                    <input
                      className="mp-input"
                      value={variant.name}
                      placeholder="Default"
                      aria-label={`Variant ${index + 1} name`}
                      onChange={event => setVariants(list => list.map(item => (item.key === variant.key ? { ...item, name: event.target.value } : item)))}
                    />
                  </td>
                  <td>
                    <input
                      className="mp-input"
                      value={variant.sku}
                      placeholder="SKU-001"
                      aria-label={`Variant ${index + 1} SKU`}
                      onChange={event => setVariants(list => list.map(item => (item.key === variant.key ? { ...item, sku: event.target.value } : item)))}
                    />
                  </td>
                  <td>
                    <input
                      className="mp-input"
                      type="number"
                      step="0.01"
                      value={variant.price}
                      placeholder={String(getValues("basePrice") || "")}
                      aria-label={`Variant ${index + 1} price override`}
                      onChange={event => setVariants(list => list.map(item => (item.key === variant.key ? { ...item, price: event.target.value } : item)))}
                    />
                  </td>
                  <td>
                    <input
                      className="mp-input"
                      type="number"
                      min="0"
                      value={variant.initialStock}
                      aria-label={`Variant ${index + 1} stock`}
                      onChange={event => setVariants(list => list.map(item => (item.key === variant.key ? { ...item, initialStock: event.target.value } : item)))}
                    />
                  </td>
                  <td>
                    <input
                      className="mp-input"
                      type="number"
                      min="0"
                      value={variant.lowStockThreshold}
                      aria-label={`Variant ${index + 1} low stock threshold`}
                      onChange={event => setVariants(list => list.map(item => (item.key === variant.key ? { ...item, lowStockThreshold: event.target.value } : item)))}
                    />
                  </td>
                  <td>
                    {variants.length > 1 ? (
                      <button
                        type="button"
                        className="btn btn-sm"
                        onClick={() => setVariants(list => list.filter(item => item.key !== variant.key))}
                        aria-label={`Remove variant ${index + 1}`}
                        style={{ color: "var(--text-subtle)" }}
                      >
                        <Trash2 size={16} aria-hidden />
                      </button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="editor-specs">
        <div className="mp-spread" style={{ marginBottom: "var(--space-3)" }}>
          <h2 className="mp-section-title" id="editor-specs" style={{ fontSize: "var(--fs-h3)", margin: 0 }}>
            Specifications
          </h2>
          <button
            type="button"
            className="btn btn-sm btn-outline-secondary"
            onClick={() => setSpecifications(list => [...list, { key: nextKey(), specKey: "", value: "" }])}
          >
            <Plus size={14} aria-hidden className="me-1" />
            Add a row
          </button>
        </div>

        <p style={{ color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
          The facts a buyer checks before ordering: dimensions, materials, what is in the box.
        </p>

        <div className="mp-stack-sm">
          {specifications.map((spec, index) => (
            <div key={spec.key} className="row g-2 align-items-end">
              <div className="col-12 col-md-5">
                <TextField
                  label={`Label ${index + 1}`}
                  placeholder="Battery life"
                  value={spec.specKey}
                  onChange={event => setSpecifications(list => list.map(item => (item.key === spec.key ? { ...item, specKey: event.target.value } : item)))}
                />
              </div>
              <div className="col-12 col-md-5">
                <TextField
                  label={`Value ${index + 1}`}
                  placeholder="40 hours"
                  value={spec.value}
                  onChange={event => setSpecifications(list => list.map(item => (item.key === spec.key ? { ...item, value: event.target.value } : item)))}
                />
              </div>
              <div className="col-12 col-md-2">
                {specifications.length > 1 ? (
                  <button
                    type="button"
                    className="btn btn-sm"
                    onClick={() => setSpecifications(list => list.filter(item => item.key !== spec.key))}
                    aria-label={`Remove specification ${index + 1}`}
                    style={{ color: "var(--text-subtle)" }}
                  >
                    <Trash2 size={16} aria-hidden />
                  </button>
                ) : null}
              </div>
            </div>
          ))}
        </div>
      </section>

      <div className="d-flex flex-wrap" style={{ gap: "0.5rem" }}>
        <button type="submit" className="btn btn-primary" disabled={submitting}>
          {submitting ? "Saving…" : isEdit ? "Save changes" : "Create this listing"}
        </button>
        <Link href="/seller/products" className="btn btn-outline-secondary">
          Cancel
        </Link>
        {!isEdit ? (
          <p style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)", alignSelf: "center" }}>
            New listings are reviewed by a moderator before shoppers can see them.
          </p>
        ) : null}
      </div>
    </form>
  );
}

function newVariant(): VariantDraft {
  return { key: nextKey(), sku: "", name: "Default", price: "", initialStock: "0", lowStockThreshold: "5" };
}

/**
 * The listing's own rows, in the form's shape.
 *
 * Used to seed the editor when it is opened on an existing listing, so the seller is looking at
 * what they have rather than at empty inputs. Each falls back to a single blank row, because a
 * listing with nothing in it is still something to edit.
 */
function draftsFromVariants(variants: ProductVariant[] | undefined): VariantDraft[] {
  if (!variants || variants.length === 0) {
    return [newVariant()];
  }

  return variants.map(variant => ({
    key: nextKey(),
    sku: variant.sku,
    name: variant.name,
    price: variant.price === null ? "" : String(variant.price),
    initialStock: String(variant.availableQuantity),
    lowStockThreshold: variant.lowStockThreshold === null ? "" : String(variant.lowStockThreshold),
  }));
}

function draftsFromImages(images: ProductImage[] | undefined): ImageDraft[] {
  if (!images || images.length === 0) {
    return [{ key: nextKey(), url: "", altText: "", isPrimary: true }];
  }

  return images.map((image, index) => ({
    key: nextKey(),
    url: image.url,
    altText: image.altText ?? "",
    isPrimary: image.isPrimary || index === 0,
  }));
}

function draftsFromSpecifications(specifications: ProductSpecification[] | undefined): SpecDraft[] {
  if (!specifications || specifications.length === 0) {
    return [{ key: nextKey(), specKey: "", value: "" }];
  }

  return specifications.map(spec => ({ key: nextKey(), specKey: spec.key, value: spec.value }));
}

let keyCounter = 0;

function nextKey(): string {
  keyCounter += 1;
  return `draft-${keyCounter}`;
}

function flattenCategories(categories: Category[]): { id: string; label: string }[] {
  return categories.flatMap(category => [
    { id: category.id, label: category.name },
    ...flattenCategories(category.children).map(child => ({ ...child, label: `— ${child.label}` })),
  ]);
}
