import {it,expect,vi} from 'vitest';
import {act} from 'react';import {createRoot} from 'react-dom/client';
import {financeBounds,GlobalFinancePeriod} from '../GlobalFinancePeriod';
vi.mock('../../../utils/apiAuth',()=>({getApiAuthHeaders:async()=>({headers:{}})}));
it('uses UTC calendar windows and an inclusive custom end date',()=>{
 const now=new Date('2026-01-15T12:00:00Z');
 expect(financeBounds('month','','',now)?.from).toBe('2026-01-01T00:00:00.000Z');
 expect(financeBounds('previous','','',now)).toEqual({from:'2025-12-01T00:00:00.000Z',to:'2026-01-01T00:00:00.000Z'});
 expect(financeBounds('quarter','','',now)?.from).toBe('2025-11-01T00:00:00.000Z');
 expect(financeBounds('year','','',now)?.from).toBe('2026-01-01T00:00:00.000Z');
 expect(financeBounds('custom','2026-02-28','2026-02-28')?.to).toBe('2026-03-01T00:00:00.000Z');
 expect(financeBounds('custom','2026-02-30','2026-03-01')).toBeNull();expect(financeBounds('custom','2026-03-02','2026-03-01')).toBeNull();
});
it('reloads all financial indicators together and waits for valid custom dates',async()=>{
 const fetch=vi.fn().mockImplementation(async()=>new Response(JSON.stringify({companies:2,machines:3,users:4,subscriptionsPaidEur:29.9,includedCreditGrantedEur:10,topUpsAddedEur:20})));vi.stubGlobal('fetch',fetch);
 const host=document.createElement('div'),root=createRoot(host);
 try{
 await act(async()=>root.render(<GlobalFinancePeriod token={async()=>null}/>));
 expect(host.textContent).toContain('29,90');expect(host.textContent).toContain('10,00');expect(host.textContent).toContain('20,00');
 expect(host.querySelectorAll('article')).toHaveLength(6);
 expect([...host.querySelectorAll('article strong')].slice(0,3).map(e=>e.textContent)).toEqual(['2','3','4']);
 expect(host.textContent).toContain('Période :');
 fetch.mockImplementationOnce(async()=>new Response(JSON.stringify({companies:0,machines:1,users:2,subscriptionsPaidEur:0,includedCreditGrantedEur:0,topUpsAddedEur:0})));
 await act(async()=>{const select=host.querySelector('select')!;select.value='previous';select.dispatchEvent(new Event('change',{bubbles:true}));});
 expect(fetch).toHaveBeenCalledTimes(2);expect(fetch.mock.calls[0][0]).not.toBe(fetch.mock.calls[1][0]);
 expect([...host.querySelectorAll('article strong')].slice(0,3).map(e=>e.textContent)).toEqual(['0','1','2']);
 await act(async()=>{const select=host.querySelector('select')!;select.value='custom';select.dispatchEvent(new Event('change',{bubbles:true}));});
 expect(host.querySelectorAll('input[type="date"]')).toHaveLength(2);expect(fetch).toHaveBeenCalledTimes(2);expect(host.textContent).toContain('Choisissez une période valide');
 }finally{await act(async()=>root.unmount());vi.unstubAllGlobals();}
});
