import { useEffect } from 'react';
import { checkoutReturnedMessage, checkoutWindowName } from '../start/checkoutPopup';

export function CheckoutReturnPage() {
  useEffect(() => {
    if (window.name !== checkoutWindowName || !window.opener) return;

    window.opener.postMessage({ type: checkoutReturnedMessage }, window.location.origin);
    window.close();
  }, []);

  return null;
}
