/** Every listing waiting for a decision, and the reasons given either way. */

import { ModerationQueue } from "@/features/admin/components/ModerationQueue";

export default function ModerationPage() {
  return (
    <div>
      <div className="mp-page-header">
        <div>
          <h1 className="mp-page-title">Moderation</h1>
          <p className="mp-page-subtitle">
            Nothing reaches a shopper until it has been here. A rejection without a reason is the one thing a seller cannot
            act on.
          </p>
        </div>
      </div>

      <ModerationQueue />
    </div>
  );
}