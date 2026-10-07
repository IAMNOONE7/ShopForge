import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { LanguageSwitcher } from "./components/LanguageSwitcher";
import { initializeI18n } from "./i18n";
import "./styles/tokens.css";
import "./styles/base.css";
import "./index.css";
import "./styles/discovery.css";
import "./styles/catalog.css";
import App from "./App.tsx";

async function render() {
  await initializeI18n();
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <div className="utility-bar">
        <LanguageSwitcher />
      </div>
      <App />
    </StrictMode>,
  );
}

void render();
