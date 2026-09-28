import { useEffect, useId, useRef, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { InlineMessage } from "./ui/InlineMessage";

export function PermissionScope({
  allowed,
  children,
}: {
  allowed: boolean;
  children: ReactNode;
}) {
  const { t } = useTranslation("auth");
  const messageId = useId();
  const scope = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (allowed || !scope.current) return;
    const disabled = new Set<
      | HTMLButtonElement
      | HTMLInputElement
      | HTMLSelectElement
      | HTMLTextAreaElement
    >();

    function disableMutationControls() {
      const controls = scope.current?.querySelectorAll<
        | HTMLButtonElement
        | HTMLInputElement
        | HTMLSelectElement
        | HTMLTextAreaElement
      >("button, input, select, textarea");
      controls?.forEach((control) => {
        if (
          control.closest(".request-error") ||
          control.hasAttribute("data-read-action") ||
          control.disabled
        )
          return;
        control.disabled = true;
        disabled.add(control);
      });
    }

    disableMutationControls();
    const observer = new MutationObserver(disableMutationControls);
    observer.observe(scope.current, { childList: true, subtree: true });
    return () => {
      observer.disconnect();
      disabled.forEach((control) => {
        if (control.isConnected) control.disabled = false;
      });
    };
  }, [allowed, children]);

  if (allowed) return children;

  return (
    <>
      <div id={messageId} className="permission-message">
        <InlineMessage tone="info">{t("readOnly")}</InlineMessage>
      </div>
      <div
        ref={scope}
        className="permission-scope"
        aria-describedby={messageId}
      >
        {children}
      </div>
    </>
  );
}
