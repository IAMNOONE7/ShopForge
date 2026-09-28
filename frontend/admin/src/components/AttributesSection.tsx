import { useTranslation } from "react-i18next";
import { api, type AttributeDefinition, type AttributeType } from "../api";
const types: AttributeType[] = [
  "select",
  "multiSelect",
  "decimal",
  "integer",
  "boolean",
  "date",
  "text",
];
type Props = {
  storeId: string;
  attributes: AttributeDefinition[];
  run: (change: () => Promise<unknown>) => Promise<void>;
};
export function AttributesSection({ storeId, attributes, run }: Props) {
  const { t } = useTranslation(["attributes", "common"]);
  async function create(form: FormData) {
    const type = String(form.get("type")) as AttributeType;
    await run(() =>
      api.createAttribute(storeId, {
        name: String(form.get("name")),
        type,
        unit: String(form.get("unit")) || null,
        isFilterable: type !== "text" && form.get("isFilterable") === "on",
        isVisibleOnProductPage: form.get("isVisibleOnProductPage") === "on",
        options: String(form.get("options") ?? "")
          .split(",")
          .map((option) => option.trim())
          .filter(Boolean),
      }),
    );
  }
  return (
    <section>
      <h2>{t("attributes:title")}</h2>
      <table>
        <thead>
          <tr>
            <th>{t("attributes:name")}</th>
            <th>{t("attributes:code")}</th>
            <th>{t("attributes:type")}</th>
            <th>{t("attributes:filter")}</th>
            <th>{t("attributes:shown")}</th>
            <th>{t("attributes:options")}</th>
          </tr>
        </thead>
        <tbody>
          {attributes.map((attribute) => (
            <tr key={attribute.id}>
              <td>
                {attribute.name}
                {attribute.unit && ` (${attribute.unit})`}
              </td>
              <td>{attribute.code}</td>
              <td>{t(`attributes:types.${attribute.type}`)}</td>
              <td>{attribute.isFilterable ? t("common:yes") : "—"}</td>
              <td>
                {attribute.isVisibleOnProductPage ? t("common:yes") : "—"}
              </td>
              <td>
                {attribute.options.map((option) => option.name).join(", ")}
                {(attribute.type === "select" ||
                  attribute.type === "multiSelect") && (
                  <form
                    action={(form) =>
                      run(() =>
                        api.addOption(
                          storeId,
                          attribute.id,
                          String(form.get("name")),
                        ),
                      )
                    }
                    className="inline-form compact"
                  >
                    <input
                      name="name"
                      placeholder={t("attributes:newOption")}
                      required
                    />
                    <button type="submit">{t("common:add")}</button>
                  </form>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <form action={create} className="inline-form">
        <input
          name="name"
          placeholder={t("attributes:attributeName")}
          required
        />
        <select name="type" defaultValue="select">
          {types.map((type) => (
            <option key={type} value={type}>
              {t(`attributes:types.${type}`)}
            </option>
          ))}
        </select>
        <input
          name="unit"
          placeholder={t("attributes:unitOptional")}
          size={8}
        />
        <input name="options" placeholder={t("attributes:optionsComma")} />
        <label>
          <input name="isFilterable" type="checkbox" defaultChecked />{" "}
          {t("attributes:filter")}
        </label>
        <label>
          <input name="isVisibleOnProductPage" type="checkbox" defaultChecked />{" "}
          {t("attributes:showOnProduct")}
        </label>
        <button type="submit">{t("attributes:addAttribute")}</button>
      </form>
    </section>
  );
}
