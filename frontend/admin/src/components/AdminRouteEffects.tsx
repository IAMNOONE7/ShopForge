import { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { useLocation } from "react-router";

export function AdminRouteEffects() {
  const { t, i18n } = useTranslation("navigation");
  const location = useLocation();
  const previousPath = useRef<string | null>(null);

  useEffect(() => {
    const main = document.getElementById("main-content");
    if (!main) return;
    const shouldFocus =
      previousPath.current !== null &&
      previousPath.current !== location.pathname;
    previousPath.current = location.pathname;
    let focused = false;

    function update() {
      const heading = main?.querySelector<HTMLElement>("h1");
      const page = heading?.textContent?.trim() || t("administration");
      document.title = t("pageTitle", { page });
      if (!shouldFocus || focused || !heading) return;
      focused = true;
      if (!heading.hasAttribute("tabindex"))
        heading.setAttribute("tabindex", "-1");
      heading.focus({ preventScroll: true });
      heading.scrollIntoView?.({ block: "start" });
    }

    update();
    const observer = new MutationObserver(update);
    observer.observe(main, {
      childList: true,
      subtree: true,
      characterData: true,
    });
    return () => observer.disconnect();
  }, [i18n.resolvedLanguage, location.pathname, t]);

  return null;
}
