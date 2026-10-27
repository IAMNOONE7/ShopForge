import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { LanguageSwitcher } from "./components/LanguageSwitcher";
import { initializeI18n } from "./i18n";
import "./styles/tokens.css";
import "./styles/base.css";
import "./index.css";
import "./styles/discovery.css";
import "./styles/catalog.css";
import "./styles/product.css";
import "./styles/shopping.css";
import "./styles/customer.css";
import App from "./App.tsx";
import { captureEmailChangeEntry } from "./emailChangeEntry";

const emailChangeEntry = captureEmailChangeEntry();

async function render() {
  await initializeI18n();
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <div className="utility-bar">
        <LanguageSwitcher />
      </div>
      <App emailChangeEntry={emailChangeEntry} />
    </StrictMode>,
  );
}

void render();
