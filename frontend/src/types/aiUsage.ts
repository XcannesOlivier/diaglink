export type UsageType = 'ChatResponse' | 'VisionTool' | 'ConversationSummary';
export interface UsageFilter { from?: string; to: string; usageType?: UsageType }
export interface AiUsageMetricsDto {
  eventCount: number; chatResponseCount: number; conversationSummaryCount: number; visionToolCount: number;
  knownUsageCount: number; unknownUsageCount: number; completedCount: number; notCompletedCount: number;
  inputTokens: number | null; outputTokens: number | null; totalTokens: number | null;
}
export interface AiUsageSummaryDto {
  from: string | null; to: string; usageType: UsageType | null; metrics: AiUsageMetricsDto;
  unassignedCompanyCount: number; unassignedMachineCount: number; unassignedUserCount: number;
}
export interface CompanyUsageDto {
  companyId: string | null; companyName: string; machineCountUsed: number; userCountUsed: number; metrics: AiUsageMetricsDto;
}
export interface MachineUsageDto {
  machineId: string | null; machineName: string; userCountUsed: number; metrics: AiUsageMetricsDto;
}
export interface UserUsageDto {
  userId: string | null; userDisplayName: string; email: string | null; metrics: AiUsageMetricsDto;
}

