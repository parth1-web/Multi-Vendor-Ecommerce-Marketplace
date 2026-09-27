/**
 * Checkout, as a sequence of small steps rather than one long form.
 *
 * Each step is one decision, and the order they are asked in is the order they have to be
 * answered in: the address decides the shipping, the shipping decides the total, and the
 * payment can only be taken once the amount is known. A single page with all of it asks for
 * payment before it has said what the payment is for.
 */

"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { z } from "zod";

import { CheckField, TextField } from "@/components/forms/FormField";
import { EmptyState, ErrorState } from "@/components/shared/Feedback";
import { useCart } from "@/features/cart/api/useCart";
import { addressApi, checkoutApi } from "@/features/orders/api/orderApi";
import { errorMessage } from "@/lib/errors";
import { cx, formatCurrency } from "@/lib/format";
import { queryKeys } from "@/lib/queryKeys";
import type { Address, Quote } from "@/types/order";

/** Payment methods the API accepts, kept here so the page and the request cannot disagree. */
const PAYMENT_METHODS = [
  { id: "Mock", label: "Card", note: "Test payment — nothing is charged" },
  { id: "CashOnDelivery", label: "Cash on delivery", note: "Pay the courier when it arrives" },
] as const;

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

type Step = "address" | "payment" | "review";

const STEPS: Step[] = ["address", "payment", "review"];

export function CheckoutWizard() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const cart = useCart();

  const [step, setStep] = useState<Step>("address");
  const [chosenAddressId, setChosenAddressId] = useState<string | null>(null);
  const [paymentMethod, setPaymentMethod] = useState<string>("Mock");
  const [couponInput, setCouponInput] = useState("");
  const [couponCode, setCouponCode] = useState<string | null>(null);
  const [placing, setPlacing] = useState(false);
  const [placeError, setPlaceError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);

  // One key per attempt at this order, and the same key for every retry of it. The API keeps a
  // key for good and returns the order it already made, which is what makes a retry safe and
  // also what makes a derived key dangerous: derive it from the address, the coupon and the
  // payment method and the customer's second order from the same address arrives as their
  // first. Success navigates away, so arriving back here is a new attempt and a new key.
  const [idempotencyKey] = useState(() => newIdempotencyKey());

  const addresses = useQuery({ queryKey: queryKeys.addresses.list(), queryFn: () => addressApi.list() });

  // The default address is the one a returning customer means, so it is derived rather than
  // stored: a value copied into state by an effect is a second copy that can disagree.
  const addressId = chosenAddressId ?? addresses.data?.find(address => address.isDefault)?.id ?? addresses.data?.[0]?.id ?? null;

  const quote = useQuery({
    queryKey: queryKeys.checkout.quote({ addressId, couponCode }),
    queryFn: () => checkoutApi.quote({ shippingAddressId: addressId, couponCode }),
    enabled: addresses.isSuccess,
  });

  const selectedAddress = useMemo(() => addresses.data?.find(address => address.id === addressId) ?? null, [addresses.data, addressId]);

  async function placeOrder() {
    if (!addressId) {
      setStep("address");
      return;
    }

    setPlacing(true);
    setPlaceError(null);

    try {
      const result = await checkoutApi.checkout({
        shippingAddressId: addressId,
        paymentMethod,
        couponCode,
        shippingMethod: null,
        customerNote: null,
        idempotencyKey,
      });

      // The basket has become an order, so every read of it is now wrong.
      queryClient.setQueryData(queryKeys.cart.detail(), null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.cart.all });
      await queryClient.invalidateQueries({ queryKey: queryKeys.orders.all });

      // Some payment providers take the customer off to their own page to approve the payment,
      // and they say so. Sending them to the order page instead would tell them the order is
      // confirmed when the money has not moved, so the provider's page goes first and the
      // order page carries where to come back to.
      if (result.paymentRequiresAction && result.paymentRedirectUrl) {
        const destination = safePaymentRedirect(result.paymentRedirectUrl);

        if (destination) {
          const withOrder = `${destination}${destination.includes("?") ? "&" : "?"}orderId=${result.orderId}`;

          // Deliberately a full page load: this leaves the application for the provider's own
          // page, which a client-side route would not do.
          // eslint-disable-next-line @next/next/no-location-assign-relative-destination
          window.location.assign(withOrder);
          return;
        }

        setPlaceError("The payment provider sent us back to a page we will not open. Your order is saved; please pay from the order page.");
        router.push(`/orders/${result.orderId}`);
        return;
      }

      router.push(`/orders/${result.orderId}?placed=1`);
    } catch (error) {
      setPlaceError(errorMessage(error, "We could not place the order. Nothing has been charged."));
    } finally {
      setPlacing(false);
    }
  }

  if (cart.isPending) {
    return <div className="mp-skeleton" style={{ height: "18rem", borderRadius: "var(--radius)" }} />;
  }

  if (cart.isError || !cart.data) {
    return <ErrorState message="We could not load your basket." />;
  }

  if (cart.data.itemCount === 0) {
    return (
      <EmptyState
        title="There is nothing to check out"
        body="Your basket is empty. Add something and come back."
        action={
          <Link href="/products" className="btn btn-sm btn-primary">
            Browse the marketplace
          </Link>
        }
      />
    );
  }

  return (
    <div className="row g-4">
      <div className="col-12 col-lg-7">
        <ol className="mp-steps" aria-label="Checkout progress">
          {STEPS.map((value, index) => (
            <li
              key={value}
              className={cx("mp-step", step === value && "is-current", index < STEPS.indexOf(step) && "is-done")}
              aria-current={step === value ? "step" : undefined}
            >
              <span aria-hidden>{index + 1}</span>
              {value === "address" ? "Delivery address" : value === "payment" ? "Payment" : "Review and place"}
            </li>
          ))}
        </ol>

        {step === "address" ? (
          <AddressStep
            addresses={addresses.data ?? []}
            isLoading={addresses.isPending}
            selectedId={addressId}
            onSelect={setChosenAddressId}
            onToggleAdd={() => setAdding(value => !value)}
            adding={adding}
            onSaved={async () => {
              setAdding(false);
              await addresses.refetch();
            }}
            onContinue={() => addressId && setStep("payment")}
          />
        ) : null}

        {step === "payment" ? (
          <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="step-payment">
            <h2 className="mp-section-title" id="step-payment" style={{ fontSize: "var(--fs-h3)" }}>
              How would you like to pay?
            </h2>

            <fieldset className="mp-stack-sm" style={{ border: 0, padding: 0, margin: 0 }}>
              <legend className="visually-hidden">Payment method</legend>

              {PAYMENT_METHODS.map(method => (
                <label
                  key={method.id}
                  className="mp-choice"
                  style={{ borderColor: paymentMethod === method.id ? "var(--brand-600)" : "var(--border)" }}
                >
                  <input
                    type="radio"
                    name="payment-method"
                    value={method.id}
                    checked={paymentMethod === method.id}
                    onChange={() => setPaymentMethod(method.id)}
                  />
                  <span>
                    <strong style={{ fontSize: "var(--fs-sm)" }}>{method.label}</strong>
                    <span style={{ display: "block", color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>{method.note}</span>
                  </span>
                </label>
              ))}
            </fieldset>

            <div className="d-flex justify-content-between mt-4">
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setStep("address")}>
                Back
              </button>
              <button type="button" className="btn btn-sm btn-primary" onClick={() => setStep("review")}>
                Review the order
              </button>
            </div>
          </section>
        ) : null}

        {step === "review" ? (
          <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="step-review">
            <h2 className="mp-section-title" id="step-review" style={{ fontSize: "var(--fs-h3)" }}>
              Check it over
            </h2>

            {placeError ? (
              <p role="alert" className="mp-alert mp-alert-danger">
                {placeError}
              </p>
            ) : null}

            {quote.isError ? (
              <ErrorState message="We could not price this order." />
            ) : (
              <OrderSummary quote={quote.data} address={selectedAddress} cart={cart.data} />
            )}

            <div className="d-flex justify-content-between mt-4">
              <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setStep("payment")} disabled={placing}>
                Back
              </button>
              <button
                type="button"
                className="btn btn-sm btn-primary"
                onClick={placeOrder}
                disabled={placing || quote.isPending || !addressId}
              >
                {placing ? "Placing your order…" : "Place the order"}
              </button>
            </div>

            <p style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", marginTop: "var(--space-3)", marginBottom: 0 }}>
              {paymentMethod === "Mock"
                ? "This is a demonstration build: the payment is simulated and no card is charged."
                : "Pay the courier in cash when your order arrives."}
            </p>
          </section>
        ) : null}
      </div>

      <div className="col-12 col-lg-5">
        <SummaryPanel
          quote={quote.data}
          isQuoting={quote.isPending}
          addressId={addressId}
          couponInput={couponInput}
          onCouponInput={setCouponInput}
          onApplyCoupon={() => setCouponCode(couponInput.trim() || null)}
          onClearCoupon={() => {
            setCouponCode(null);
            setCouponInput("");
          }}
          onContinue={() => addressId && setStep("payment")}
          showContinue={step === "address"}
        />
      </div>
    </div>
  );
}

function AddressStep({
  addresses,
  isLoading,
  selectedId,
  onSelect,
  onToggleAdd,
  adding,
  onSaved,
  onContinue,
}: {
  addresses: Address[];
  isLoading: boolean;
  selectedId: string | null;
  onSelect: (id: string) => void;
  onToggleAdd: () => void;
  adding: boolean;
  onSaved: () => Promise<void>;
  onContinue: () => void;
}) {
  return (
    <section className="mp-card" style={{ padding: "var(--space-4)" }} aria-labelledby="step-address">
      <h2 className="mp-section-title" id="step-address" style={{ fontSize: "var(--fs-h3)" }}>
        Where should this go?
      </h2>

      {isLoading ? (
        <div className="mp-skeleton" style={{ height: "8rem", borderRadius: "var(--radius)" }} />
      ) : addresses.length > 0 ? (
        <fieldset className="mp-stack-sm" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend className="visually-hidden">Choose a delivery address</legend>

          {addresses.map(address => (
            <label
              key={address.id}
              className="mp-choice"
              style={{ borderColor: address.id === selectedId ? "var(--brand-600)" : "var(--border)" }}
            >
              <input type="radio" name="address" checked={address.id === selectedId} onChange={() => onSelect(address.id)} />
              <span>
                <strong style={{ fontSize: "var(--fs-sm)" }}>
                  {address.label}
                  {address.isDefault ? " (default)" : ""}
                </strong>
                <span style={{ display: "block", color: "var(--text-muted)", fontSize: "var(--fs-xs)" }}>{formatAddress(address)}</span>
              </span>
            </label>
          ))}
        </fieldset>
      ) : (
        <p className="mp-alert mp-alert-info">You have no saved addresses yet. Add one to continue.</p>
      )}

      <div className="d-flex justify-content-between mt-4">
        <button type="button" className="btn btn-sm btn-outline-secondary" onClick={onToggleAdd}>
          {adding ? "Choose a saved address" : "Add a new address"}
        </button>
        <button type="button" className="btn btn-sm btn-primary" onClick={onContinue} disabled={!selectedId}>
          Continue to payment
        </button>
      </div>

      {adding ? <AddressForm onSaved={onSaved} /> : null}
    </section>
  );
}

function AddressForm({ onSaved }: { onSaved: () => Promise<void> }) {
  const queryClient = useQueryClient();
  const [formError, setFormError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<AddressValues>({
    resolver: zodResolver(addressSchema),
    defaultValues: {
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
    },
  });

  async function onSubmit(values: AddressValues) {
    setFormError(null);

    try {
      await addressApi.create({
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
      });

      await queryClient.invalidateQueries({ queryKey: queryKeys.addresses.all });
      await onSaved();
    } catch (error) {
      setFormError(errorMessage(error));
    }
  }

  return (
    <form
      onSubmit={handleSubmit(onSubmit)}
      noValidate
      className="mp-stack mt-4"
      style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-4)" }}
    >
      <h3 className="mp-section-title" style={{ fontSize: "var(--fs-h3)" }}>
        Add an address
      </h3>

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

      <TextField
        label="Country code"
        autoComplete="country"
        required
        hint="Two letters, like NP or US."
        maxLength={2}
        error={errors.country?.message}
        {...register("country")}
      />

      <CheckField label="Use this as my default address" {...register("isDefault")} />

      <button type="submit" className="btn btn-sm btn-primary" disabled={isSubmitting}>
        {isSubmitting ? "Saving…" : "Save this address"}
      </button>
    </form>
  );
}

function OrderSummary({
  quote,
  address,
  cart,
}: {
  quote?: Quote;
  address: Address | null;
  cart: {
    groups: { storeName: string; items: { productName: string; variantName: string; quantity: number }[] }[];
  };
}) {
  return (
    <div className="mp-stack">
      {address ? (
        <p style={{ fontSize: "var(--fs-sm)", color: "var(--text-muted)", margin: 0 }}>
          Delivering to <strong style={{ color: "var(--text)" }}>{address.recipientName}</strong>, {formatAddress(address)}
        </p>
      ) : null}

      {cart.groups.map(group => (
        <div key={group.storeName} style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-3)" }}>
          <p className="mp-metric-label" style={{ margin: 0 }}>
            {group.storeName}
          </p>
          <ul className="list-unstyled mb-0" style={{ fontSize: "var(--fs-sm)" }}>
            {group.items.map((item, index) => (
              <li key={`${item.productName}-${index}`} className="d-flex justify-content-between">
                <span style={{ color: "var(--text-muted)" }}>
                  {item.quantity} × {item.productName} <span style={{ color: "var(--text-subtle)" }}>({item.variantName})</span>
                </span>
              </li>
            ))}
          </ul>
        </div>
      ))}

      {quote ? <Totals quote={quote} /> : null}
    </div>
  );
}

function Totals({ quote }: { quote: Quote }) {
  return (
    <dl className="mp-stack-sm mb-0" style={{ fontSize: "var(--fs-sm)" }} aria-live="polite">
      <div className="d-flex justify-content-between">
        <dt className="mp-metric-label">Subtotal</dt>
        <dd className="mb-0">{formatCurrency(quote.subtotal, quote.currency)}</dd>
      </div>

      {quote.discountAmount > 0 ? (
        <div className="d-flex justify-content-between" style={{ color: "var(--success)" }}>
          <dt className="mp-metric-label">Discount{quote.coupon ? ` (${quote.coupon.code})` : ""}</dt>
          <dd className="mb-0">−{formatCurrency(quote.discountAmount, quote.currency)}</dd>
        </div>
      ) : null}

      <div className="d-flex justify-content-between">
        <dt className="mp-metric-label">Shipping</dt>
        <dd className="mb-0">{quote.shippingAmount === 0 ? "Free" : formatCurrency(quote.shippingAmount, quote.currency)}</dd>
      </div>

      {quote.taxAmount > 0 ? (
        <div className="d-flex justify-content-between">
          <dt className="mp-metric-label">Tax</dt>
          <dd className="mb-0">{formatCurrency(quote.taxAmount, quote.currency)}</dd>
        </div>
      ) : null}

      <div className="d-flex justify-content-between" style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-2)" }}>
        <dt style={{ fontWeight: 600 }}>Total</dt>
        <dd className="mb-0 mp-price">{formatCurrency(quote.totalAmount, quote.currency)}</dd>
      </div>

      {quote.coupon && !quote.coupon.isValid ? <p className="mp-alert mp-alert-warning">{quote.coupon.message}</p> : null}
      {quote.message && !quote.coupon ? <p style={{ color: "var(--text-muted)", margin: 0 }}>{quote.message}</p> : null}
    </dl>
  );
}

function SummaryPanel({
  quote,
  isQuoting,
  addressId,
  couponInput,
  onCouponInput,
  onApplyCoupon,
  onClearCoupon,
  onContinue,
  showContinue,
}: {
  quote?: Quote;
  isQuoting: boolean;
  addressId: string | null;
  couponInput: string;
  onCouponInput: (value: string) => void;
  onApplyCoupon: () => void;
  onClearCoupon: () => void;
  onContinue: () => void;
  showContinue: boolean;
}) {
  return (
    <div
      className="mp-card"
      style={{ padding: "var(--space-4)", position: "sticky", top: "calc(var(--header-height) + var(--space-4))" }}
    >
      <h2 className="mp-section-title" style={{ fontSize: "var(--fs-h3)" }}>
        Order summary
      </h2>

      {quote ? (
        <Totals quote={quote} />
      ) : (
        <div className="mp-skeleton" style={{ height: "6rem", borderRadius: "var(--radius)" }} />
      )}

      <div className="mp-stack-sm mt-3" style={{ borderTop: "1px solid var(--border)", paddingTop: "var(--space-3)" }}>
        <label htmlFor="coupon" className="mp-metric-label">
          Coupon code
        </label>
        <div className="d-flex" style={{ gap: "0.5rem" }}>
          <input
            id="coupon"
            value={couponInput}
            onChange={event => onCouponInput(event.target.value.toUpperCase())}
            placeholder="WELCOME10"
            aria-describedby="coupon-hint"
            style={{
              flex: 1,
              padding: "0.5rem 0.65rem",
              borderRadius: "var(--radius-sm)",
              border: "1px solid var(--border)",
              backgroundColor: "var(--surface)",
              color: "var(--text)",
              textTransform: "uppercase",
            }}
          />
          {quote?.coupon ? (
            <button type="button" className="btn btn-sm btn-outline-secondary" onClick={onClearCoupon}>
              Remove
            </button>
          ) : (
            <button type="button" className="btn btn-sm btn-outline-secondary" onClick={onApplyCoupon} disabled={!couponInput.trim() || isQuoting}>
              Apply
            </button>
          )}
        </div>
        <p id="coupon-hint" style={{ margin: 0, color: "var(--text-subtle)", fontSize: "var(--fs-xs)" }}>
          A coupon is applied to the whole basket, across every seller.
        </p>
        {quote?.coupon?.isValid && quote.coupon.discountAmount > 0 ? (
          <p className="mp-alert mp-alert-success" role="status">
            {quote.coupon.code} applied.
          </p>
        ) : null}
      </div>

      {showContinue ? (
        <button type="button" className="btn btn-primary w-100 mt-3" onClick={onContinue} disabled={!addressId}>
          Continue
        </button>
      ) : null}

      <p style={{ color: "var(--text-subtle)", fontSize: "var(--fs-xs)", marginTop: "var(--space-3)", marginBottom: 0 }}>
        {addressId ? "Shipping and tax are confirmed above before you place the order." : "Choose a delivery address and we will price the shipping."}
      </p>
    </div>
  );
}

function formatAddress(address: Address): string {
  return [address.line1, address.line2, address.city, address.state, address.postalCode, address.country]
    .filter(part => part && part.trim().length > 0)
    .join(", ");
}

/** A key for one attempt at placing an order, and no two attempts ever share one. */
function newIdempotencyKey(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  return `checkout-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
}

/**
 * A payment redirect we are willing to send a customer to, or null.
 *
 * The URL arrives in a response body, and following a URL from a response body is how an
 * application turns into an open redirect: a scheme of javascript: or data: turns the checkout
 * page into whatever the string says it is. Only an ordinary web address is accepted, and
 * anything else falls back to the order page, which is a worse experience and a safer one.
 */
function safePaymentRedirect(url: string): string | null {
  try {
    const parsed = new URL(url);

    return parsed.protocol === "https:" || parsed.protocol === "http:" ? parsed.toString() : null;
  } catch {
    return null;
  }
}
