import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { LanguageSwitcher } from "./components/LanguageSwitcher";
import { initializeI18n } from "./i18n";
import "./styles/tokens.css";
import "./styles/base.css";
import "./index.css";
import App from "./App.tsx";
import { captureInvitationEntry } from "./invitationEntry";
import { captureStaffRecoveryEntry } from "./staffRecoveryEntry";
import { captureOperatorRecoveryEntry } from "./platform/operatorRecoveryEntry";

const operatorRecoveryEntry = captureOperatorRecoveryEntry();
const recoveryEntry = captureStaffRecoveryEntry();
const invitationEntry = captureInvitationEntry();

async function render() {
  await initializeI18n();
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <div className="utility-bar">
        <LanguageSwitcher />
      </div>
      <App invitationEntry={invitationEntry} recoveryEntry={recoveryEntry} operatorRecoveryEntry={operatorRecoveryEntry} />
    </StrictMode>,
  );
}

void render();
