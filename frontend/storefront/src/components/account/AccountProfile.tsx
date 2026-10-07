import { useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { updateProfile, type Customer } from "../../account";
import { profileIssues, type AuthIssue } from "../../auth";
import { AuthValidationSummary } from "../auth/AuthValidationSummary";
import { Button } from "../ui/Button";
import { Field } from "../ui/Field";
import { InlineMessage } from "../ui/InlineMessage";
import { RequestError } from "../ui/RequestError";

export function AccountProfile({
  customer,
  onUpdated,
}: {
  customer: Customer;
  onUpdated: (customer: Customer) => void;
}) {
  const { t } = useTranslation(["account", "auth", "validation"]);
  const [firstName, setFirstName] = useState(customer.firstName);
  const [lastName, setLastName] = useState(customer.lastName);
  const [phone, setPhone] = useState(customer.phone ?? "");
  const [issues, setIssues] = useState<AuthIssue[]>([]);
  const [error, setError] = useState<unknown | null>(null);
  const [saved, setSaved] = useState(false);
  const [pending, setPending] = useState(false);
  const lock = useRef(false);

  function changed(field: AuthIssue["field"]) {
    setSaved(false);
    setIssues((current) => current.filter((issue) => issue.field !== field));
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (lock.current) return;

    const draft = {
      firstName: firstName.trim(),
      lastName: lastName.trim(),
      phone: phone.trim() || null,
    };
    const invalid = profileIssues(draft);
    setIssues(invalid);
    setSaved(false);
    setError(null);
    if (invalid.length > 0) return;

    lock.current = true;
    setPending(true);
    try {
      const updated = await updateProfile(draft);
      setFirstName(updated.firstName);
      setLastName(updated.lastName);
      setPhone(updated.phone ?? "");
      onUpdated(updated);
      setSaved(true);
    } catch (caught) {
      setError(caught);
    } finally {
      setPending(false);
      lock.current = false;
    }
  }

  return (
    <section
      className="account-panel account-profile"
      aria-labelledby="account-profile-heading"
    >
      <div className="account-section-heading">
        <div>
          <h2 id="account-profile-heading" tabIndex={-1}>{t("account:details")}</h2>
          <p>{t("account:detailsIntro")}</p>
        </div>
      </div>
      <form onSubmit={(event) => void save(event)} aria-busy={pending} noValidate>
        <AuthValidationSummary issues={issues} />
        <Field
          label={t("auth:firstName")}
          name="firstName"
          autoComplete="given-name"
          maxLength={100}
          required
          disabled={pending}
          value={firstName}
          onChange={(event) => {
            setFirstName(event.currentTarget.value);
            changed("firstName");
          }}
          error={
            issues.some((issue) => issue.field === "firstName")
              ? t("validation:nameInvalid")
              : null
          }
        />
        <Field
          label={t("auth:lastName")}
          name="lastName"
          autoComplete="family-name"
          maxLength={100}
          required
          disabled={pending}
          value={lastName}
          onChange={(event) => {
            setLastName(event.currentTarget.value);
            changed("lastName");
          }}
          error={
            issues.some((issue) => issue.field === "lastName")
              ? t("validation:nameInvalid")
              : null
          }
        />
        <Field
          label={t("auth:email")}
          name="email"
          type="email"
          autoComplete="email"
          value={customer.email}
          readOnly
          hint={t("account:emailReadOnly")}
        />
        <Field
          label={t("auth:phoneOptional")}
          name="phone"
          type="tel"
          autoComplete="tel"
          maxLength={30}
          disabled={pending}
          value={phone}
          onChange={(event) => {
            setPhone(event.currentTarget.value);
            setSaved(false);
          }}
        />
        <Button
          type="submit"
          busy={pending}
          busyLabel={t("account:saving")}
        >
          {t("account:saveDetails")}
        </Button>
        {saved && (
          <InlineMessage tone="success">
            <p>{t("account:saved")}</p>
          </InlineMessage>
        )}
        {error !== null && <RequestError error={error} operation="write" />}
      </form>
    </section>
  );
}
