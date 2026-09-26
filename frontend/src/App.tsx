import { useEffect, useState } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { ErrorBoundary } from './components/core/ErrorBoundary';
import { AuthenticatedApp } from './components/AuthenticatedApp';
import { useAppAuthentication } from './hooks/useAppAuthentication';
import { LandingPage } from './pages/landing/LandingPage';
import { ContactPage } from './pages/contact/ContactPage';
import { PrivacyPage } from './pages/privacy/PrivacyPage';
import { LegalNoticePage } from './pages/legal/LegalNoticePage';
import { DemonstrationPage } from './pages/demonstration/DemonstrationPage';
import { StartPage } from './pages/start/StartPage';
import { CheckoutReturnPage } from './pages/checkout-return/CheckoutReturnPage';
import { LoginPage } from './pages/login/LoginPage';
import { InstallShortcutDialog } from './components/marketing/InstallShortcutDialog';
import { ScrollToTop } from './components/core/ScrollToTop';
import { pendingInstallPlatformKey, type ShortcutPlatform } from './utils/installShortcut';
import './App.css';

function App() {
  const authentication = useAppAuthentication();
  const location = useLocation();
  const [installDialog, setInstallDialog] = useState<ShortcutPlatform | null>(null);

  useEffect(() => {
    if (!authentication.isAuthenticated || location.pathname !== '/app') return;
    const pending = sessionStorage.getItem(pendingInstallPlatformKey);
    if (pending !== 'windows' && pending !== 'ios' && pending !== 'android' && pending !== 'other') return;
    sessionStorage.removeItem(pendingInstallPlatformKey);
    setInstallDialog(pending);
  }, [authentication.isAuthenticated, location.pathname]);

  return (
    <ErrorBoundary>
      <ScrollToTop />
      <Routes>
        <Route path="/" element={<LandingPage isAuthenticated={authentication.isAuthenticated} />} />
        <Route path="/contact" element={<ContactPage isAuthenticated={authentication.isAuthenticated} />} />
        <Route path="/confidentialite" element={<PrivacyPage isAuthenticated={authentication.isAuthenticated} />} />
        <Route path="/mentions-legales" element={<LegalNoticePage isAuthenticated={authentication.isAuthenticated} />} />
        <Route path="/demonstration" element={<DemonstrationPage isAuthenticated={authentication.isAuthenticated} />} />
        <Route path="/commencer" element={<StartPage isAuthenticated={authentication.isAuthenticated} />} />
        <Route path="/app/checkout-return" element={<CheckoutReturnPage />} />
        <Route
          path="/login"
          element={
            !authentication.isCheckingSession && authentication.isAuthenticated
              ? <Navigate to="/app" replace />
              : <LoginPage {...authentication} />
          }
        />
        <Route
          path="/app/*"
          element={
            authentication.isCheckingSession
              ? <AuthenticatedApp loadingOnly />
              : authentication.isAuthenticated
                ? <AuthenticatedApp onDiagLinkSessionExpired={authentication.expireDiagLinkSession} />
                : <Navigate to="/login" replace />
          }
        />
        <Route path="/administration" element={<Navigate to="/app/administration" replace />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      <InstallShortcutDialog platform={installDialog} onClose={() => setInstallDialog(null)} />
    </ErrorBoundary>
  );
}

export default App;
