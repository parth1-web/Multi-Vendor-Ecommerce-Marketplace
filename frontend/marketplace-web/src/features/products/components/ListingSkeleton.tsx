/**
 * The listing's shape before it has loaded, so the page does not jump when it does.
 *
 * A server-rendered page that suspends on nothing still has to show something while the client
 * island boots, and a spinner is a worse answer than the layout the shopper is about to read.
 */

export function ListingSkeleton() {
  return (
    <div className="row g-4">
      <div className="col-12 col-lg-3">
        <div className="mp-skeleton" style={{ height: "20rem", borderRadius: "var(--radius)" }} />
      </div>
      <div className="col-12 col-lg-9">
        <div className="row g-3">
          {Array.from({ length: 8 }, (_, index) => (
            <div key={index} className="col-6 col-md-4 col-xl-3">
              <div className="mp-skeleton" style={{ height: "16rem", borderRadius: "var(--radius)" }} />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
