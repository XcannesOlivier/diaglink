/** DTOs mirroring backend/WebApp.Api/Models/CompanyModels.cs exactly. */
export interface CompanyDto {
  id: string;
  name: string;
  status: string;
}

/** Minimal dbo.Users projection — deliberately excludes any Entra identifier. */
export interface CompanyUserDto {
  id: string;
  email: string;
  role: string;
  status: string;
  firstName?: string | null;
  lastName?: string | null;
  phoneNumber?: string | null;
}

/** Mirrors backend/WebApp.Api/Models/CompanyOnboardingModels.cs CompanyOnboardingResultDto. */
export interface CompanyOnboardingResultDto {
  company: CompanyDto;
  admin: CompanyUserDto;
}
