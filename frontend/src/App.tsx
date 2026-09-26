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
import { ScrollToTop } from './components/core/ScrollToTop';
import './App.css';

function App() {
  const authentication = useAppAuthentication();
  const location = useLocation();
  const isInstallShortcutRequest = location.pathname === '/login'
    && new URLSearchParams(location.search).get('install') === '1';

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
            !isInstallShortcutRequest && !authentication.isCheckingSession && authentication.isAuthenticated
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
    </ErrorBoundary>
  );
}

export default App;
