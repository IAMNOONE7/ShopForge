import { useId, useState } from "react";
import { useTranslation } from "react-i18next";

export function PasswordField({
  name = "password",
  label,
  autoComplete,
  hint,
  invalid = false,
  disabled = false,
}: {
  name?: string;
  label: React.ReactNode;
  autoComplete: "current-password" | "new-password";
  hint?: React.ReactNode;
  invalid?: boolean;
  disabled?: boolean;
}) {
  const { t } = useTranslation("auth");
  const id = useId();
  const hintId = hint ? `${id}-hint` : undefined;
  const [shown, setShown] = useState(false);

  return (
    <div className="auth-field">
      <label htmlFor={id}>{label}</label>
      <div className="password-control">
        <input
          id={id}
          name={name}
          type={shown ? "text" : "password"}
          autoComplete={autoComplete}
          minLength={autoComplete === "new-password" ? 10 : undefined}
          maxLength={128}
          required
          disabled={disabled}
          aria-invalid={invalid || undefined}
          aria-describedby={hintId}
        />
        <button
          type="button"
          className="password-reveal"
          aria-controls={id}
          aria-pressed={shown}
          disabled={disabled}
          onClick={() => setShown((current) => !current)}
        >
          {t(shown ? "hidePassword" : "showPassword")}
        </button>
      </div>
      {hint && <span id={hintId} className="hint">{hint}</span>}
    </div>
  );
}
