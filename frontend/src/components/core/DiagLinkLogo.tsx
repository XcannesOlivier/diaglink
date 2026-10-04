import type { ImgHTMLAttributes } from 'react';
import lightLogo from '../../assets/Logo DiagLink.png';
import darkLogo from '../../assets/Logo DiagLink Sombre.png';
import { useThemeContext } from '../../contexts/ThemeContext';

type DiagLinkLogoProps = Omit<ImgHTMLAttributes<HTMLImageElement>, 'src'>;

export function DiagLinkLogo({ alt = 'DiagLink', ...imageProps }: DiagLinkLogoProps) {
  const { isDarkMode } = useThemeContext();

  return <img {...imageProps} src={isDarkMode ? darkLogo : lightLogo} alt={alt} />;
}
