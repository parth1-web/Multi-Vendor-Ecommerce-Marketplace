/**
 * The address book.
 *
 * The same form the checkout uses, on a page of its own, because the two are the same decision:
 * somebody who needs an address in the middle of checkout will need a second one later, and
 * being made to find it here is how people end up with one bad address forever.
 */

"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { Modal } from "react-bootstrap";
import { z } from "zod";

import { CheckField, TextField } from "@/components/forms/FormField";
import { EmptyState } from "@/components/shared/Feedback";
import { RequireAuth } from "@/features/account/components/RequireAuth";
import { addressApi } from "@/features/orders/api/orderApi";
import { errorMessage, fieldErrors } from "@/lib/errors";
import { formatDate } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import { useToast } from "@/providers/ToastProvider";
import type { Address, CreateAddressRequest } from "@/types/order";

const addressSchema = z.object({
  label: z.string().trim().min(1, "Give this address a name, like Home or Office.").max(60),
  recipientName: z.string().trim().min(1, "Who should the parcel be addressed to?").max(120),
  phoneNumber: z.string().trim().regex(/^[+0-9 ()-]{6,20}$/, "Enter a phone number the courier can reach."),
  line1: z.string().trim().min(1, "Enter the street and number."),
  line2: z.string().trim().optional(),
  city: z.string().trim().min(1, "Enter a city."),
  state: z.string().trim().optional(),
  postalCode: z.string().trim().min(1, "Enter a postal code."),
  country: z.string().trim().length(2, "Use a two-letter country code, like NP or US."),
  isDefault: z.boolean(),
});

type AddressValues = z.infer<typeof addressSchema>;

function AddressBook() {
  const queryClient = useQueryClient();
  const { push } = useToast();
  const [editing, setEditing] = useState<Address | null>(null);
  const [adding, setAdding] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<Address | null>(null);

  const addresses = useQuery({ queryKey: queryKeys.addresses.list(), queryFn: () => addressApi.list() });

  const save = useMutation({
    mutationFn: ({ id, values }: { id: string | null; values: CreateAddressRequest }) =>
      id ? addressApi.update(id, values) : addressApi.create(values),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.addresses.all });
      setAdding(false);
      setEditing(null);
    },
  });

  const remove = useMutation({
    mutationFn: (id: string) => addressApi.remove(id),
    onSuccess: async (_, id) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.addresses.all });
      setDeleting(null);
      const label = addresses.data?.find((address) => address.id === id)?.label;
      push({ tone: "success", title: label ? `Deleted the ${label} address` : "Address deleted" });
    },
    onError: (failure) =>
      push({ tone: "danger", title: "Could not delete the address", body: errorMessage(failure) }),
  });

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<AddressValues>({
    resolver: zodResolver(addressSchema),
    defaultValues: emptyAddress(),
  });

  function startEditing(address: Address) {
    setAdding(false);
    setEditing(address);
    setFormError(null);
    reset({
      label: address.label,
      recipientName: address.recipientName,
      phoneNumber: address.phoneNumber,
      line1: address.line1,
      line2: address.line2 ?? "",
      city: address.city,
      state: address.state ?? "",
      postalCode: address.postalCode,
      country: address.country,
      isDefault: address.isDefault,
    });
  }

  function startAdding() {
    setEditing(null);
    setAdding(true);
    setFormError(null);
    reset(emptyAddress());
  }

  async function onSubmit(values: AddressValues) {
    setFormError(null);

    const payload: CreateAddressRequest = {
      label: values.label,
      recipientName: values.recipientName,
      phoneNumber: values.phoneNumber,
      line1: values.line1,
      line2: values.line2?.trim() || null,
      city: values.city,
      state: values.state?.trim() || null,
      postalCode: values.postalCode,
      country: values.country.toUpperCase(),
      isDefault: values.isDefault,
    };

    try {
      await save.mutateAsync({ id: editing?.id ?? null, values: payload });
    } catch (error) {
      for (const [field, message] of Object.entries(fieldErrors(error))) {
        const key = field.toLowerCase();
        if (key in values) {
          setError(key as keyof AddressValues, { message });
        }
      }

      setFormError(errorMessage(error));
    }
  }

  const formOpen = adding || editing !== null;

  return (
    <div className="mp-stack">
      {addresses.isPending ? (
        <div className="mp-skeleton" style={{ height: "10rem", borderRadius: "var(--radius)" }} />
      ) : addresses.data && addresses.data.length === 0 && !formOpen ? (
        <EmptyState
          title="No addresses yet"
          body="Add one now and checkout will use it by default."
          action={
            <button type="button" className="btn btn-sm btn-primary" onClick={startAdding}>
              Add an address
            </button>
          }
        />
      ) : (
        <div className="row g-3">
          {addresses.data?.map(address => (
            <div key={address.id} className="col-12 col-lg-6">
              <article className="mp-card" style={{ padding: "var(--space-4)", height: "100%" }}>
                <div className="mp-spread">
                  <h2 style={{ margin: 0, fontSize: "var(--fs-h3)" }}>
                    {address.label}
                    {address.isDefault ? (
                      <span style={{ marginLeft: "0.5rem", color: "var(--text-subtle)", fontSize: "var(--fs-xs)", fontWeight: 400 }}>
                        default
                      </span>
                    ) : null}
                  </h2>
                </div>

                <p style={{ margin: "var(--space-2) 0 0", fontSize: "var(--fs-sm)", color: "var(--text-muted)" }}>
                  {address.recipientName}
                  <br />
                  {address.phoneNumber}
                  <br />
                  {formatAddress(address)}
                </p>

                <p style={{ margin: "var(--space-2) 0 0", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
                  Added {formatDate(address.createdAt)}
                </p>

                <div className="d-flex" style={{ gap: "0.5rem" }}>
                  <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => startEditing(address)}>
                    Edit
                  </button>
                  <button
                    type="button"
                    className="btn btn-sm"
                    onClick={() => setDeleting(address)}
                    aria-label={`Delete the ${address.label} address`}
                    style={{ color: "var(--text-subtle)" }}
                  >
                    <Trash2 size={16} aria-hidden />
                  </button>
                </div>
              </article>
            </div>
          ))}
        </div>
      )}

      {!formOpen && addresses.data && addresses.data.length > 0 ? (
        <div>
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={startAdding}>
            Add another address
          </button>
        </div>
      ) : null}

      {formOpen ? (
        <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="address-form">
          <h2 className="mp-section-title" id="address-form" style={{ fontSize: "var(--fs-h3)" }}>
            {editing ? `Edit ${editing.label}` : "Add an address"}
          </h2>

          <form onSubmit={handleSubmit(onSubmit)} noValidate className="mp-stack">
            {formError ? (
              <p role="alert" className="mp-alert mp-alert-danger">
                {formError}
              </p>
            ) : null}

            <div className="row g-2">
              <div className="col-12 col-sm-6">
                <TextField label="Name for this address" required error={errors.label?.message} {...register("label")} />
              </div>
              <div className="col-12 col-sm-6">
                <TextField label="Recipient" autoComplete="name" required error={errors.recipientName?.message} {...register("recipientName")} />
              </div>
            </div>

            <TextField label="Phone" type="tel" autoComplete="tel" required error={errors.phoneNumber?.message} {...register("phoneNumber")} />

            <TextField label="Street and number" autoComplete="address-line1" required error={errors.line1?.message} {...register("line1")} />
            <TextField label="Apartment, floor, landmark" autoComplete="address-line2" hint="Optional, but it helps a courier." {...register("line2")} />

            <div className="row g-2">
              <div className="col-12 col-sm-5">
                <TextField label="City" autoComplete="address-level2" required error={errors.city?.message} {...register("city")} />
              </div>
              <div className="col-12 col-sm-3">
                <TextField label="State or region" autoComplete="address-level1" {...register("state")} />
              </div>
              <div className="col-12 col-sm-4">
                <TextField label="Postal code" autoComplete="postal-code" required error={errors.postalCode?.message} {...register("postalCode")} />
              </div>
            </div>

            <TextField label="Country code" autoComplete="country" required hint="Two letters, like NP or US." maxLength={2} error={errors.country?.message} {...register("country")} />

            <CheckField label="Use this as my default address" {...register("isDefault")} />

            <div className="d-flex" style={{ gap: "0.5rem" }}>
              <button type="submit" className="btn btn-sm btn-primary" disabled={isSubmitting || save.isPending}>
                {isSubmitting ? "Saving…" : editing ? "Save changes" : "Save this address"}
              </button>
              <button
                type="button"
                className="btn btn-sm btn-outline-secondary"
                onClick={() => {
                  setAdding(false);
                  setEditing(null);
                }}
              >
                Cancel
              </button>
            </div>
          </form>
        </section>
      ) : null}

      <Modal show={deleting !== null} onHide={() => setDeleting(null)} centered aria-labelledby="delete-address-title">
        <Modal.Header closeButton>
          <Modal.Title id="delete-address-title">Delete this address?</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p style={{ margin: 0, color: "var(--text-muted)", fontSize: "var(--fs-sm)" }}>
            {deleting ? (
              <>
                The <strong style={{ color: "var(--text)" }}>{deleting.label}</strong> address will be removed.
                This action cannot be undone.
              </>
            ) : null}
          </p>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-outline-secondary" onClick={() => setDeleting(null)}>
            Cancel
          </button>
          <button
            type="button"
            className="btn btn-danger"
            disabled={remove.isPending || !deleting}
            aria-busy={remove.isPending}
            onClick={() => deleting && remove.mutate(deleting.id)}
          >
            {remove.isPending ? "Deleting…" : "Delete address"}
          </button>
        </Modal.Footer>
      </Modal>
    </div>
  );
}

function emptyAddress(): AddressValues {
  return {
    label: "Home",
    recipientName: "",
    phoneNumber: "",
    line1: "",
    line2: "",
    city: "",
    state: "",
    postalCode: "",
    country: "NP",
    isDefault: false,
  };
}

function formatAddress(address: Address): string {
  return [address.line1, address.line2, address.city, address.state, address.postalCode, address.country]
    .filter(part => part && part.trim().length > 0)
    .join(", ");
}

export function AddressesPage() {
  return (
    <RequireAuth returnTo="/addresses">
      <AddressBook />
    </RequireAuth>
  );
}
