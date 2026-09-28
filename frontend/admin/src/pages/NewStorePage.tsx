import { useTranslation } from "react-i18next";
import { useNavigate, useOutletContext } from "react-router";
import { api } from "../api";
import { useAction } from "../useAction";
import { RequestError } from "../components/ui/RequestError";
type LayoutContext = { reloadStores: () => void };
export function NewStorePage() {
  const { t } = useTranslation(["stores", "errors"]);
  const navigate = useNavigate();
  const { reloadStores } = useOutletContext<LayoutContext>();
  const [error, run] = useAction(reloadStores);
  async function create(form: FormData) {
    await run(async () => {
      const store = await api.createStore({
        name: String(form.get("name")),
        hostName: String(form.get("hostName")),
        currency: String(form.get("currency")).toUpperCase(),
        culture: String(form.get("culture")),
        theme: {
          primaryColor: String(form.get("primaryColor")),
          secondaryColor: String(form.get("secondaryColor")),
          borderRadius: Number(form.get("borderRadius")),
        },
      });
      void navigate(`/stores/${store.id}`);
    });
  }
  return (
    <>
      <h1>{t("stores:newTitle")}</h1>
      <p className="hint">{t("stores:newHint")}</p>
      <form action={create} className="stack edit-form">
        <label>
          {t("stores:name")} <input name="name" required />
        </label>
        <label>
          {t("stores:address")}{" "}
          <input name="hostName" placeholder="shop.example.com" required />
        </label>
        <label>
          {t("stores:currency")}{" "}
          <input name="currency" defaultValue="EUR" maxLength={3} required />
        </label>
        <label>
          {t("stores:formattingCulture")}{" "}
          <input name="culture" defaultValue="en-IE" required />
        </label>
        <label>
          {t("stores:primaryColor")}{" "}
          <input name="primaryColor" type="color" defaultValue="#1F6FEB" />
        </label>
        <label>
          {t("stores:secondaryColor")}{" "}
          <input name="secondaryColor" type="color" defaultValue="#EEF4FF" />
        </label>
        <label>
          {t("stores:cornerRadius")}{" "}
          <input
            name="borderRadius"
            type="number"
            min="0"
            max="32"
            defaultValue="6"
          />
        </label>
        <button type="submit">{t("stores:create")}</button>
        {error !== null && <RequestError error={error} operation="write" />}
      </form>
    </>
  );
}
