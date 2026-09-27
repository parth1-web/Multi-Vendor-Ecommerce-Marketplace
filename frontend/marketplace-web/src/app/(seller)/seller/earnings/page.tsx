/** What a seller has earned, what is on its way, and what the marketplace has taken. */

import { SellerEarnings } from "@/features/seller/components/SellerEarnings";

export default function Page() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Earnings</h1>
          <p className="mp-page-subtitle">Gross, commission, and what is left for you.</p>
        </div>
      </div>

      <SellerEarnings />
    </div>
  );
}
