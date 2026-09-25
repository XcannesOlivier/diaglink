import { Button, Input, Spinner } from '@fluentui/react-components';
import { useNavigate } from 'react-router-dom';
import logoDiagLink from '../../assets/Logo DiagLink.png';
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
  handleContinue: () => Promise<void>;
  handleVerifyCode: (code?: string) => Promise<boolean>;
  handleChangeEmail: () => void;
}

export function LoginPage({ isCheckingSession, email, setEmail, emailCheckMessage, isCheckingEmail, showCodeStep, code, setCode, handleContinue, handleVerifyCode, handleChangeEmail }: LoginPageProps) {
  const navigate = useNavigate();

  const verifyAndNavigate = async (value?: string) => {
    if (await handleVerifyCode(value)) navigate('/app', { replace: true });
  };

  return (
    <div className={authStyles.authScreen}>
      <div className={authStyles.authCard}>
        <img src={logoDiagLink} alt="DiagLink" className={authStyles.authLogo} />
        <h1 className={authStyles.authTitle}>Assistant Technique</h1>
        <p className={authStyles.authSubtitle}>Votre support technique pour la maintenance industrielle</p>
        <p className={authStyles.authDescription}>Accédez rapidement aux informations de vos équipements et facilitez vos diagnostics.</p>

        {isCheckingSession ? (
          <Spinner size="medium" label="Vérification de la session..." />
        ) : !showCodeStep ? (
          <>
            <div className={authStyles.fieldGroup}>
              <label className={authStyles.fieldLabel}>Adresse e-mail</label>
              <Input type="email" name="email" autoComplete="email" inputMode="email" size="large" value={email} onChange={(_, data) => setEmail(data.value)} className={authStyles.fullWidthInput} />
              {emailCheckMessage && <p className={authStyles.fieldMessage}>{emailCheckMessage}</p>}
            </div>
            <Button appearance="primary" size="large" disabled={!email.trim() || isCheckingEmail} onClick={() => void handleContinue()} className={authStyles.primaryButton}>Continuer</Button>
          </>
        ) : (
          <>
            <p className={authStyles.otpIntro}>Un code de connexion a été envoyé à<br /><strong>{email}</strong></p>
            <div className={authStyles.fieldGroup}>
              <label className={authStyles.fieldLabel}>Code de connexion</label>
              <Input
                type="text" inputMode="numeric" autoComplete="one-time-code" maxLength={6} size="large" value={code}
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
                className={authStyles.fullWidthInput}
              />
            </div>
            <Button appearance="primary" size="large" onClick={() => void verifyAndNavigate()} disabled={code.length !== 6} className={authStyles.primaryButton}>Se connecter</Button>
            <Button appearance="transparent" size="small" onClick={handleChangeEmail} className={authStyles.changeEmailButton}>Changer d'adresse e-mail</Button>
          </>
        )}

        <p className={authStyles.secureNote}>Connexion sécurisée</p>
        <div className={authStyles.poweredBy}><span><span className={authStyles.poweredByText}>Propulsé par </span>Microsoft Foundry</span></div>
      </div>
    </div>
  );
}
