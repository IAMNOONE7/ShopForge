import { useState } from "react";
import { Link } from "react-router";
import { useStore } from "../storeContext";

export function StoreBrand() {
  const store = useStore();
  return (
    <Link to="/" className="store-brand" lang={store.culture}>
      {store.logoUrl ? (
        <StoreLogo key={store.logoUrl} url={store.logoUrl} name={store.name} />
      ) : store.name}
    </Link>
  );
}

function StoreLogo({ url, name }: { url: string; name: string }) {
  const [failed, setFailed] = useState(false);
  return failed ? name : (
    <img
      src={url}
      alt={name}
      className="store-logo"
      width={240}
      height={64}
      onError={() => setFailed(true)}
    />
  );
}
