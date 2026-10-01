import type { CSSProperties } from "react";
import { useTranslation } from "react-i18next";
import { Field } from "./ui/Field";
import { issueText } from "./storeValidation";
import type { StoreIssue, ThemeDraft } from "./storeValidation";

type Props = {
  value: ThemeDraft;
  storeName: string;
  issues: StoreIssue[];
  onChange: (field: keyof ThemeDraft, value: string) => void;
};

const validColor = /^#[0-9a-f]{6}$/i;

export function StoreThemeEditor({
  value,
  storeName,
  issues,
  onChange,
}: Props) {
  const { t } = useTranslation("stores");
  return (
    <div className="theme-editor">
      <div className="theme-fields">
        <ColorField
          name="primaryColor"
          label={t("primaryColor")}
          value={value.primaryColor}
          fallback="#1F6FEB"
          error={issueText(issues, "primaryColor", t)}
          onChange={(color) => onChange("primaryColor", color)}
        />
        <ColorField
          name="secondaryColor"
          label={t("secondaryColor")}
          value={value.secondaryColor}
          fallback="#EEF4FF"
          error={issueText(issues, "secondaryColor", t)}
          onChange={(color) => onChange("secondaryColor", color)}
        />
        <Field
          id="borderRadius"
          name="borderRadius"
          type="number"
          min="0"
          step="1"
          label={t("cornerRadius")}
          hint={t("cornerRadiusHint")}
          value={value.borderRadius}
          error={issueText(issues, "borderRadius", t)}
          onChange={(event) => onChange("borderRadius", event.target.value)}
        />
      </div>
      <ThemePreview value={value} storeName={storeName} />
    </div>
  );
}

function ColorField({
  name,
  label,
  value,
  fallback,
  error,
  onChange,
}: {
  name: "primaryColor" | "secondaryColor";
  label: string;
  value: string;
  fallback: string;
  error?: string;
  onChange: (value: string) => void;
}) {
  const { t } = useTranslation("stores");
  const previewColor = validColor.test(value) ? value : fallback;
  const textColor = name === "primaryColor" ? "#FFFFFF" : "#182230";
  const ratio = contrastRatio(previewColor, textColor);
  const passes = ratio >= 4.5;
  const contrastId = `${name}-contrast`;

  return (
    <div className="color-field">
      <Field
        id={name}
        name={name}
        label={label}
        value={value}
        maxLength={7}
        spellCheck={false}
        autoCapitalize="characters"
        aria-describedby={contrastId}
        error={error}
        onChange={(event) => onChange(event.target.value)}
      />
      <label className="color-picker" htmlFor={`${name}-picker`}>
        <span>{t("chooseColor", { color: label })}</span>
        <input
          id={`${name}-picker`}
          type="color"
          value={previewColor}
          onChange={(event) => onChange(event.target.value.toUpperCase())}
        />
      </label>
      <p
        id={contrastId}
        className={passes ? "contrast-pass" : "contrast-review"}
      >
        {t(passes ? "contrastPass" : "contrastReview", {
          ratio: ratio.toFixed(1),
        })}
      </p>
    </div>
  );
}

export function ThemePreview({
  value,
  storeName,
}: {
  value: ThemeDraft;
  storeName: string;
}) {
  const { t } = useTranslation("stores");
  const primary = validColor.test(value.primaryColor)
    ? value.primaryColor
    : "#1F6FEB";
  const secondary = validColor.test(value.secondaryColor)
    ? value.secondaryColor
    : "#EEF4FF";
  const radius = /^\d+$/.test(value.borderRadius)
    ? Math.min(Number(value.borderRadius), 48)
    : 6;
  const style = {
    "--preview-primary": primary,
    "--preview-secondary": secondary,
    "--preview-radius": `${radius}px`,
    "--preview-primary-text": bestTextColor(primary),
    "--preview-secondary-text": bestTextColor(secondary),
  } as CSSProperties;

  return (
    <aside className="theme-preview" style={style} aria-label={t("preview")}>
      <span className="theme-preview-label">{t("preview")}</span>
      <div className="theme-preview-header">
        <strong>{storeName.trim() || t("previewStoreName")}</strong>
      </div>
      <div className="theme-preview-body">
        <div className="theme-preview-card">
          <span>{t("previewProduct")}</span>
          <strong>{t("previewPrice")}</strong>
          <span className="theme-preview-button">{t("previewAction")}</span>
        </div>
      </div>
    </aside>
  );
}

function bestTextColor(color: string) {
  return contrastRatio(color, "#FFFFFF") >= contrastRatio(color, "#182230")
    ? "#FFFFFF"
    : "#182230";
}

function contrastRatio(left: string, right: string) {
  const first = luminance(left);
  const second = luminance(right);
  return (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05);
}

function luminance(color: string) {
  const channels = [1, 3, 5].map((index) => {
    const raw = Number.parseInt(color.slice(index, index + 2), 16) / 255;
    return raw <= 0.03928 ? raw / 12.92 : ((raw + 0.055) / 1.055) ** 2.4;
  });
  return (
    channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722
  );
}
