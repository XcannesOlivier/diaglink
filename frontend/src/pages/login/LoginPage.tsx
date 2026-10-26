import { useState } from 'react';
import { Button, Input, Spinner } from '@fluentui/react-components';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { AIFoundryLogo } from '../../components/icons/AIFoundryLogo';
import { InstallShortcutDialog } from '../../components/marketing/InstallShortcutDialog';
import { detectShortcutPlatform, type ShortcutPlatform } from '../../utils/installShortcut';
import { PUBLIC_ORIGIN } from '../../config/origins';
import loginLogo from '../../assets/Logo DiagLink1.png';
import authStyles from '../../App.module.css';

interface LoginPageProps {
  isCheckingSession: boolean;
  email: string;
  setEmail: (email: string) => void;
  emailCheckMessage: string | null;
  isCheckingEmail: boolean;
  showCodeStep: boolean;
  code: string;
  setCode: (code: string) => void;
  codeCheckMessage: string | null;
  isVerifyingCode: boolean;
  handleContinue: () => Promise<void>;
  handleVerifyCode: (code?: string) => Promise<boolean>;
  handleChangeEmail: () => void;
}

export function LoginPage({ isCheckingSession, email, setEmail, emailCheckMessage, isCheckingEmail, showCodeStep, code, setCode, codeCheckMessage, isVerifyingCode, handleContinue, handleVerifyCode, handleChangeEmail }: LoginPageProps) {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [installDialog, setInstallDialog] = useState<ShortcutPlatform | null>(() =>
    searchParams.get('install') === '1' ? detectShortcutPlatform() : null);

  const verifyAndNavigate = async (value?: string) => {
    if (await handleVerifyCode(value)) navigate('/app', { replace: true });
  };

  const closeInstallDialog = () => {
    setInstallDialog(null);
    if (searchParams.get('install') !== '1') return;
    const nextSearchParams = new URLSearchParams(searchParams);
    nextSearchParams.delete('install');
    setSearchParams(nextSearchParams, { replace: true });
  };

  return (
    <div className={authStyles.authScreen}>
      <div className={authStyles.authCard}>
        <img src={loginLogo} alt="DiagLink" className={authStyles.authLogo} />
        <h1 className={authStyles.authTitle}>Assistant Technique</h1>
        <p className={authStyles.authSubtitle}>Votre support technique pour la maintenance industrielle</p>
        <p className={authStyles.authDescription}>Accédez rapidement aux informations de vos équipements et facilitez vos diagnostics.</p>

        {isCheckingSession ? (
          <Spinner size="medium" label="Vérification de la session..." />
        ) : !showCodeStep ? (
          <form onSubmit={event => { event.preventDefault(); void handleContinue(); }}>
            <div className={authStyles.fieldGroup}>
              <label htmlFor="diaglink-login-email" className={authStyles.fieldLabel}>Adresse e-mail</label>
              <Input id="diaglink-login-email" type="email" name="email" autoComplete="email" inputMode="email" required size="large" value={email} onChange={(_, data) => setEmail(data.value)} aria-invalid={Boolean(emailCheckMessage)} aria-describedby={emailCheckMessage ? 'diaglink-login-email-message' : undefined} className={authStyles.fullWidthInput} />
              {emailCheckMessage && <p id="diaglink-login-email-message" role="alert" className={authStyles.fieldMessage}>{emailCheckMessage}</p>}
            </div>
            <Button type="submit" appearance="primary" size="large" disabled={!email.trim() || isCheckingEmail} className={authStyles.primaryButton}>
              {isCheckingEmail ? <><Spinner size="tiny" /> Envoi du code...</> : 'Continuer'}
            </Button>
          </form>
        ) : (
          <form onSubmit={event => { event.preventDefault(); void verifyAndNavigate(); }}>
            <p className={authStyles.otpIntro}>Un code de connexion a été envoyé à<br /><strong>{email}</strong></p>
            <div className={authStyles.fieldGroup}>
              <label htmlFor="diaglink-login-code" className={authStyles.fieldLabel}>Code de connexion</label>
              <Input
                id="diaglink-login-code" name="one-time-code" type="text" inputMode="numeric" autoComplete="one-time-code" maxLength={6} size="large" value={code}
                onChange={(_, data) => {
                  const newCode = data.value.replace(/\D/g, '').slice(0, 6);
                  setCode(newCode);
                  if (newCode.length === 6) {
                    if (window.matchMedia('(max-width: 1000px)').matches) {
                      setTimeout(() => { if (document.activeElement instanceof HTMLElement) document.activeElement.blur(); }, 100);
                    }
                    void verifyAndNavigate(newCode);
                  }
                }}
                disabled={isVerifyingCode}
                aria-invalid={Boolean(codeCheckMessage)}
                aria-describedby={codeCheckMessage ? 'diaglink-login-code-message' : undefined}
                className={authStyles.fullWidthInput}
              />
              {codeCheckMessage && <p id="diaglink-login-code-message" role="alert" className={authStyles.fieldMessage}>{codeCheckMessage}</p>}
            </div>
            <Button type="submit" appearance="primary" size="large" disabled={code.length !== 6 || isVerifyingCode} className={authStyles.primaryButton}>
              {isVerifyingCode ? <><Spinner size="tiny" /> Vérification...</> : 'Se connecter'}
            </Button>
            <Button type="button" appearance="transparent" size="small" onClick={handleChangeEmail} disabled={isVerifyingCode} className={authStyles.changeEmailButton}>Changer d'adresse e-mail</Button>
          </form>
        )}

        <p className={authStyles.secureNote}>Connexion sécurisée</p>
        <a href={`${PUBLIC_ORIGIN}/`} className={authStyles.returnHomeLink}>Site DiagLink</a>
        <Button appearance="subtle" size="small" className={authStyles.installShortcutButton}
          onClick={() => setInstallDialog(detectShortcutPlatform())}>Installer DiagLink</Button>
        <div className={authStyles.poweredBy}>
          <AIFoundryLogo className={authStyles.foundryLogo} width={16} height={16} />
          <span>Propulsé par Microsoft Foundry</span>
        </div>
      </div>
      <InstallShortcutDialog platform={installDialog} showOpenLoginButton={false}
        onClose={closeInstallDialog} />
    </div>
  );
}
