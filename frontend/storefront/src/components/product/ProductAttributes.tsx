import { useTranslation } from "react-i18next";
import type { ProductAttribute } from "../../api";
import type { Store } from "../../store";
import { formatProductAttribute } from "./productAttribute";

export function ProductAttributes({
  attributes,
  store,
}: {
  attributes: ProductAttribute[];
  store: Store;
}) {
  const { t } = useTranslation(["catalog", "common"]);

  if (attributes.length === 0) return null;

  return (
    <section className="product-section" aria-labelledby="product-details-heading">
      <h2 id="product-details-heading" tabIndex={-1}>{t("catalog:productDetails")}</h2>
      <div className="product-attributes-wrap">
        <table className="product-attributes">
          <tbody>
            {attributes.map((attribute) => (
              <tr key={attribute.code}>
                <th scope="row" lang={store.culture}>
                  {attribute.name}
                </th>
                <td lang={attribute.type === "boolean" ? undefined : store.culture}>
                  {formatProductAttribute(
                    attribute,
                    store,
                    t("common:yes"),
                    t("common:no"),
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
