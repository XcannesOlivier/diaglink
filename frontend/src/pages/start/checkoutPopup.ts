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
