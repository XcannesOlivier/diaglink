import { useEffect, useRef } from 'react';
import { useLocation } from 'react-router-dom';

const publicPaths = new Set([
  '/',
  '/commencer',
  '/contact',
  '/confidentialite',
  '/demonstration',
  '/mentions-legales',
]);

export function ScrollToTop() {
  const { pathname } = useLocation();
  const previousPathname = useRef(pathname);

  useEffect(() => {
    if (previousPathname.current !== pathname && publicPaths.has(pathname)) {
      window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
    }
    previousPathname.current = pathname;
  }, [pathname]);

  return null;
}