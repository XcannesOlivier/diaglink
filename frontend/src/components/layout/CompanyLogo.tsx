import { useState } from 'react';
import { makeStyles, mergeClasses } from '@fluentui/react-components';

const useStyles = makeStyles({
  logo: {
    display: 'block',
    height: 'auto',
    width: 'auto',
    maxHeight: '28px',
    maxWidth: '96px',
    objectFit: 'contain',
    flexShrink: 0,
  },
});

interface CompanyLogoProps {
  logoObjectUrl: string | null;
  companyName: string | null | undefined;
  className?: string;
}

/** Displays the existing session asset; never downloads or owns its Object URL. */
export function CompanyLogo({ logoObjectUrl, companyName, className }: CompanyLogoProps) {
  const styles = useStyles();
  const [failedUrl, setFailedUrl] = useState<string | null>(null);

  if (!logoObjectUrl || failedUrl === logoObjectUrl) return null;

  return (
    <img
      className={mergeClasses(styles.logo, className)}
      src={logoObjectUrl}
      alt={companyName?.trim() ? `Logo ${companyName}` : 'Logo de l’entreprise'}
      onError={() => setFailedUrl(logoObjectUrl)}
    />
  );
}
