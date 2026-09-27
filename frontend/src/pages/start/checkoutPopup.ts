import { APP_ORIGIN, PUBLIC_ORIGIN } from '../../config/origins';

export const checkoutWindowName = 'diaglink-machine-request-checkout';
export const checkoutReturnedMessage = 'diaglink:machine-request-checkout-returned';
export const authorizationConfirmedMessage = 'diaglink:machine-request-authorization-confirmed';
export const requestSubmittedMessage = 'diaglink:machine-request-submitted';

export function isCheckoutReturnWindow() {
  return window.name === checkoutWindowName;
}

export function isTrustedCheckoutMessage(event: MessageEvent, popup: Window | null, type: string) {
  return event.origin === window.location.origin
    && popup !== null
    && event.source === popup
    && Boolean(event.data)
    && typeof event.data === 'object'
    && event.data.type === type;
}

export function isTrustedPublicCheckoutMessage(event: MessageEvent, popup: Window | null, type: string) {
  return trustedPublicCheckoutOrigins().has(event.origin)
    && popup !== null
    && event.source === popup
    && Boolean(event.data)
    && typeof event.data === 'object'
    && event.data.type === type;
}

export function postPublicCheckoutMessage(target: Window, type: string) {
  for (const origin of trustedPublicCheckoutOrigins()) target.postMessage({ type }, origin);
}

export function isTrustedPublicCheckoutOpenerMessage(event: MessageEvent, opener: Window, type: string) {
  return trustedPublicCheckoutOrigins().has(event.origin)
    && event.source === opener
    && Boolean(event.data)
    && typeof event.data === 'object'
    && event.data.type === type;
}

function trustedPublicCheckoutOrigins() {
  return new Set([window.location.origin, PUBLIC_ORIGIN, APP_ORIGIN]);
}
