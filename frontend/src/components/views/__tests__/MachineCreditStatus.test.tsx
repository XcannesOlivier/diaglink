import {beforeEach,afterEach,it,expect,vi} from 'vitest';
import {act} from 'react';
import {createRoot,type Root} from 'react-dom/client';
import {MachineCreditStatus} from '../MachineCreditStatus';
import {getErrorCodeFromResponse,createAppError} from '../../../utils/errorHandler';
vi.mock('../../../utils/apiAuth',()=>({getApiAuthHeaders:async()=>({headers:{}})}));
let box:HTMLDivElement;let root:Root;
beforeEach(()=>{box=document.createElement('div');document.body.append(box);root=createRoot(box);});
afterEach(async()=>{await act(async()=>root.unmount());box.remove();vi.unstubAllGlobals();});
it('loads credit automatically and reloads on machine change without a refresh button',async()=>{
 const fetch=vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({message:'Crédit IA épuisé. Rechargez le portefeuille.'})))
 .mockResolvedValueOnce(new Response(JSON.stringify({message:'Crédit entreprise disponible.'})));
 vi.stubGlobal('fetch',fetch);
 const token=async()=>null;
 await act(async()=>root.render(<MachineCreditStatus machineId="m" getAccessToken={token}/>));
 expect(box.textContent).toContain('Rechargez');
 expect(box.querySelector('button')).toBeNull();
 await act(async()=>root.render(<MachineCreditStatus machineId="other-machine" getAccessToken={token}/>));
 expect(fetch).toHaveBeenCalledTimes(2);
 expect(fetch.mock.calls[1][0]).toContain('/machines/other-machine/ai-credit');
 expect(box.querySelector('button')).toBeNull();
 expect(box.textContent).toContain('Crédit entreprise disponible');
});
it('maps credit refusal to a recharge message rather than authentication or retry',()=>{
 const code=getErrorCodeFromResponse(new Response('',{status:402}));
 expect(code).toBe('AiCreditExhausted');
 const error=createAppError(new Error(),code);expect(error.message).toContain('Rechargez');expect(error.recoverable).toBe(false);
});
