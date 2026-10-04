export type MonthlyQuotaStatus='Épuisé'|'Non actif'|null;

export function getMonthlyQuotaStatus(resetUtc:string|null|undefined,remaining:number|null|undefined):MonthlyQuotaStatus {
 if(!resetUtc)return 'Non actif';
 return remaining!==null&&remaining!==undefined&&remaining<=0?'Épuisé':null;
}
