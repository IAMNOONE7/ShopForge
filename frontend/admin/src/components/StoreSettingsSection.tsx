import { useTranslation } from "react-i18next";
import { api, type AdminStore, type StoreTheme } from "../api";
type Props = {
  store: AdminStore;
  theme: StoreTheme;
  run: (change: () => Promise<unknown>) => Promise<void>;
};
export function StoreSettingsSection({ store, theme, run }: Props) {
  const { t } = useTranslation("stores");
  async function save(form: FormData) {
    await run(() =>
      api.updateStore(store.id, {
        name: String(form.get("name")),
        currency: String(form.get("currency")).toUpperCase(),
        culture: String(form.get("culture")),
        returnWindowDays: Number(form.get("returnWindowDays")),
        company: {
          legalName: String(form.get("legalName")),
          line1: String(form.get("line1")),
          city: String(form.get("city")),
          postalCode: String(form.get("postalCode")),
          country: String(form.get("country")).toUpperCase(),
          registrationNumber: String(form.get("registrationNumber")),
          vatNumber: String(form.get("vatNumber")) || null,
        },
        theme: {
          primaryColor: String(form.get("primaryColor")),
          secondaryColor: String(form.get("secondaryColor")),
          borderRadius: Number(form.get("borderRadius")),
        },
      }),
    );
  }
  return (
    <section>
      <h2>{t("settings")}</h2>
      <form
        action={save}
        className="stack edit-form"
        key={`${store.name}-${store.currency}-${store.culture}-${store.company?.legalName ?? ""}`}
      >
        <label>
          {t("name")} <input name="name" defaultValue={store.name} required />
        </label>
        <label>
          {t("currency")}{" "}
          <input
            name="currency"
            defaultValue={store.currency}
            maxLength={3}
            required
            disabled={store.status === "published"}
          />
        </label>
        {store.status === "published" && (
          <p className="hint">{t("publishedCurrencyHint")}</p>
        )}
        <label>
          {t("formattingCulture")}{" "}
          <input name="culture" defaultValue={store.culture} required />
        </label>
        <label>
          {t("returnWindow")}{" "}
          <input
            name="returnWindowDays"
            type="number"
            min="0"
            max="365"
            defaultValue={store.returnWindowDays}
            required
          />
        </label>
        <label>
          {t("primaryColor")}{" "}
          <input
            name="primaryColor"
            type="color"
            defaultValue={theme.primaryColor}
          />
        </label>
        <label>
          {t("secondaryColor")}{" "}
          <input
            name="secondaryColor"
            type="color"
            defaultValue={theme.secondaryColor}
          />
        </label>
        <label>
          {t("cornerRadius")}{" "}
          <input
            name="borderRadius"
            type="number"
            min="0"
            max="32"
            defaultValue={theme.borderRadius}
          />
        </label>
        <fieldset>
          <legend>{t("companyDetails")}</legend>
          <p className="hint">{t("companyHint")}</p>
          <label>
            {t("legalName")}{" "}
            <input
              name="legalName"
              defaultValue={store.company?.legalName ?? store.name}
              required
            />
          </label>
          <label>
            {t("street")}{" "}
            <input
              name="line1"
              defaultValue={store.company?.line1 ?? ""}
              required
            />
          </label>
          <label>
            {t("city")}{" "}
            <input
              name="city"
              defaultValue={store.company?.city ?? ""}
              required
            />
          </label>
          <label>
            {t("postalCode")}{" "}
            <input
              name="postalCode"
              defaultValue={store.company?.postalCode ?? ""}
              required
            />
          </label>
          <label>
            {t("country")}{" "}
            <input
              name="country"
              defaultValue={store.company?.country ?? ""}
              maxLength={2}
              required
            />
          </label>
          <label>
            {t("registrationNumber")}{" "}
            <input
              name="registrationNumber"
              defaultValue={store.company?.registrationNumber ?? ""}
              required
            />
          </label>
          <label>
            {t("vatNumber")}{" "}
            <input
              name="vatNumber"
              defaultValue={store.company?.vatNumber ?? ""}
            />
          </label>
        </fieldset>
        <button type="submit">{t("saveSettings")}</button>
      </form>
    </section>
  );
}
