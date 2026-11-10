import { lazy, Suspense } from 'react';
import { Spinner } from '@fluentui/react-components';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { ErrorBoundary } from './components/core/ErrorBoundary';
import { useAppAuthentication } from './hooks/useAppAuthentication';
import { LandingPage } from './pages/landing/LandingPage';
import { ScrollToTop } from './components/core/ScrollToTop';
import './App.css';

const AuthenticatedApp = lazy(() => import('./components/AuthenticatedApp').then(module => ({
  default: module.AuthenticatedApp,
})));

const LoginPage = lazy(() => import('./pages/login/LoginPage').then(module => ({
  default: module.LoginPage,
})));

const ContactPage = lazy(() => import('./pages/contact/ContactPage').then(module => ({ default: module.ContactPage })));
const PrivacyPage = lazy(() => import('./pages/privacy/PrivacyPage').then(module => ({ default: module.PrivacyPage })));
const LegalNoticePage = lazy(() => import('./pages/legal/LegalNoticePage').then(module => ({ default: module.LegalNoticePage })));
const ConditionsPage = lazy(() => import('./pages/conditions/ConditionsPage').then(module => ({ default: module.ConditionsPage })));
const DemonstrationPage = lazy(() => import('./pages/demonstration/DemonstrationPage').then(module => ({ default: module.DemonstrationPage })));
const StartPage = lazy(() => import('./pages/start/StartPage').then(module => ({ default: module.StartPage })));
const CheckoutReturnPage = lazy(() => import('./pages/checkout-return/CheckoutReturnPage').then(module => ({ default: module.CheckoutReturnPage })));

const authenticatedAppFallback = (
  <div className="app-container app-loading"><Spinner size="large" /><p>Chargement...</p></div>
);

const publicPageFallback = (
  <div className="app-container app-loading"><Spinner size="large" /><p>Chargement de la page...</p></div>
);

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
        <Route path="/contact" element={<Suspense fallback={publicPageFallback}><ContactPage isAuthenticated={authentication.isAuthenticated} /></Suspense>} />
        <Route path="/confidentialite" element={<Suspense fallback={publicPageFallback}><PrivacyPage isAuthenticated={authentication.isAuthenticated} /></Suspense>} />
        <Route path="/mentions-legales" element={<Suspense fallback={publicPageFallback}><LegalNoticePage isAuthenticated={authentication.isAuthenticated} /></Suspense>} />
        <Route path="/conditions" element={<Suspense fallback={publicPageFallback}><ConditionsPage isAuthenticated={authentication.isAuthenticated} /></Suspense>} />
        <Route path="/demonstration" element={<Suspense fallback={publicPageFallback}><DemonstrationPage isAuthenticated={authentication.isAuthenticated} /></Suspense>} />
        <Route path="/commencer" element={<Suspense fallback={publicPageFallback}><StartPage isAuthenticated={authentication.isAuthenticated} /></Suspense>} />
        <Route path="/app/checkout-return" element={<Suspense fallback={publicPageFallback}><CheckoutReturnPage /></Suspense>} />
        <Route
          path="/login"
          element={
            !isInstallShortcutRequest && !authentication.isCheckingSession && authentication.isAuthenticated
              ? <Navigate to="/app" replace />
              : <Suspense fallback={publicPageFallback}><LoginPage {...authentication} /></Suspense>
          }
        />
        <Route
          path="/app/*"
          element={
            <Suspense fallback={authenticatedAppFallback}>
              {authentication.isCheckingSession
                ? <AuthenticatedApp loadingOnly />
                : authentication.isAuthenticated
                  ? <AuthenticatedApp onDiagLinkSessionExpired={authentication.expireDiagLinkSession} />
                  : <Navigate to="/login" replace />}
            </Suspense>
          }
        />
        <Route path="/administration" element={<Navigate to="/app/administration" replace />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </ErrorBoundary>
  );
}

export default App;
