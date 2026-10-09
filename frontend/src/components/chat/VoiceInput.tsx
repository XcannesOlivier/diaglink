import { useState, useRef, useCallback, useEffect } from 'react';
import { Button, Tooltip, Toast, ToastTitle, Toaster, useId, useToastController } from '@fluentui/react-components';
import { MicRegular, MicOffRegular } from '@fluentui/react-icons';
import styles from './VoiceInput.module.css';

interface VoiceInputProps {
  onTranscript: (text: string) => void;
  disabled?: boolean;
}

export const VoiceInput: React.FC<VoiceInputProps> = ({ onTranscript, disabled = false }) => {
  const [isListening, setIsListening] = useState(false);
  const recognitionRef = useRef<SpeechRecognition | null>(null);

  useEffect(() => {
    return () => {
      recognitionRef.current?.abort();
    };
  }, []);

  const toasterId = useId('voice-toaster');
  const { dispatchToast } = useToastController(toasterId);

  const toggleListening = useCallback(() => {
    const SpeechRecognitionCtor = window.SpeechRecognition || window.webkitSpeechRecognition;

    if (!SpeechRecognitionCtor) {
      dispatchToast(
        <Toast>
          <ToastTitle>La saisie vocale n'est pas prise en charge par ce navigateur</ToastTitle>
        </Toast>,
        { intent: 'warning' },
      );
      return;
    }

    if (recognitionRef.current) {
      recognitionRef.current.abort();
      recognitionRef.current = null;
      setIsListening(false);
      return;
    }

    const recognition = new SpeechRecognitionCtor();
    recognition.continuous = false;
    recognition.interimResults = false;
    recognition.lang = 'fr-FR';

    recognition.onresult = (event: SpeechRecognitionEvent) => {
      const transcript = event.results?.[0]?.[0]?.transcript;
      if (transcript) onTranscript(transcript);
    };

    recognition.onend = () => {
      recognitionRef.current = null;
      setIsListening(false);
    };

    recognition.onerror = (event: SpeechRecognitionErrorEvent) => {
      recognitionRef.current = null;
      setIsListening(false);

      const msg =
        event.error === 'not-allowed' ? 'Accès au microphone refusé. Vérifiez les autorisations du navigateur.' :
        event.error === 'no-speech'   ? 'Aucune parole détectée. Veuillez réessayer.' :
        event.error === 'network'     ? 'Erreur réseau lors de la saisie vocale.' :
        undefined;

      if (msg) {
        dispatchToast(
          <Toast><ToastTitle>{msg}</ToastTitle></Toast>,
          { intent: 'error' },
        );
      }
    };

    recognitionRef.current = recognition;
    recognition.start();
    setIsListening(true);
  }, [onTranscript, dispatchToast]);

  return (
    <>
      <Toaster toasterId={toasterId} position="top-end" />
      <Tooltip content="Microphone" relationship="label" withArrow>
        <Button
          appearance="subtle"
          icon={isListening ? <MicOffRegular /> : <MicRegular />}
          onClick={toggleListening}
          disabled={disabled}
          aria-label="Microphone"
          aria-pressed={isListening}
          data-company-accent-exempt={isListening || undefined}
          className={`${styles.voiceButton} ${isListening ? styles.listening : ''}`}
        >
          {isListening && <span className={styles.pulsingDot} />}
        </Button>
      </Tooltip>
    </>
  );
};
