import { getAppConfig, createLoginRequest } from "../authConfig";
import { apiUrl } from "../utils/routing";
import type { IPublicClientApplication } from "@azure/msal-browser";

let msalInstance: IPublicClientApplication | null = null;

export function setMsalInstance(instance: IPublicClientApplication) {
  msalInstance = instance;
}

async function getAccessToken(): Promise<string> {
  if (!msalInstance) throw new Error("MSAL not initialized");

  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) throw new Error("No accounts found");

  const response = await msalInstance.acquireTokenSilent({
    ...createLoginRequest(getAppConfig()),
    account: accounts[0],
  });
  return response.accessToken;
}

async function apiFetch<T>(
  url: string,
  options: RequestInit = {}
): Promise<T> {
  const token = await getAccessToken();
  const response = await fetch(url, {
    ...options,
    headers: {
      ...options.headers,
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(`${response.status}: ${text}`);
  }
  const contentType = response.headers.get("content-type");
  if (contentType?.includes("application/json")) {
    return response.json();
  }
  return response.text() as unknown as T;
}

// --- Types ---

export interface BlobStorageLocation {
  storageAccount: string;
  blobContainer: string;
  blobName: string;
}

export type WorkflowStatus = "NotStarted" | "Started" | "ExitSuccess" | "ExitFailure";

export type WorkflowStepType =
  | "Anonymization"
  | "CLOEAnalysis"
  | "FencillaAnalysis"
  | "ThermalReadingAnalysis";

export interface AnonymizationStepData {
  isPersonInImage?: boolean | null;
  preProcessedBlobStorageLocation?: BlobStorageLocation | null;
}

export interface CLOEStepData {
  oilLevel?: number | null;
  confidence?: number | null;
}

export interface FencillaStepData {
  isBreak?: boolean | null;
  confidence?: number | null;
}

export interface ThermalReadingStepData {
  temperature?: number | null;
}

export interface WorkflowStep {
  id: string;
  type: WorkflowStepType;
  sourceBlobStorageLocation: BlobStorageLocation;
  destinationBlobStorageLocation: BlobStorageLocation;
  dateCreated: string;
  status: WorkflowStatus;
  anonymizationData?: AnonymizationStepData | null;
  cloeData?: CLOEStepData | null;
  fencillaData?: FencillaStepData | null;
  thermalReadingData?: ThermalReadingStepData | null;
}

export interface PlantWorkflow {
  id: string;
  steps: WorkflowStep[];
}

export interface PlantData {
  id: string;
  inspectionId: string;
  installationCode: string;
  dateCreated: string;
  tag?: string | null;
  coordinates?: string | null;
  inspectionDescription?: string | null;
  robotName?: string | null;
  timestamp?: string | null;
  workflow?: PlantWorkflow | null;
}

export function getWorkflowStep(
  workflow: PlantWorkflow | null | undefined,
  type: WorkflowStepType
): WorkflowStep | undefined {
  return workflow?.steps.find((step) => step.type === type);
}

export function getPlantDataWorkflowStep(
  plantData: PlantData,
  type: WorkflowStepType
): WorkflowStep | undefined {
  return getWorkflowStep(plantData.workflow, type);
}

export interface AnalysisMapping {
  id: string;
  tag: string;
  inspectionDescription: string;
  analysesToBeRun: AnalysisType[];
}

export type AnalysisType =
  | "ConstantLevelOiler"
  | "Fencilla"
  | "ThermalReading";

export interface PlantDataRequest {
  inspectionId: string;
  installationCode: string;
  tagId: string;
  inspectionDescription: string;
  rawDataBlobStorageLocation: BlobStorageLocation;
}

// --- API calls ---

export interface PagedResponse<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface PlantDataFilterParams {
  inspectionId?: string;
  tag?: string;
  installationCode?: string;
  anonymizationStatus?: string;
  analysisType?: string;
  hasIncompleteWorkflows?: boolean;
}

export async function getPlantData(
  pageNumber = 1,
  pageSize = 25,
  filters: PlantDataFilterParams = {}
): Promise<PagedResponse<PlantData>> {
  const params = new URLSearchParams({
    PageNumber: String(pageNumber),
    PageSize: String(pageSize),
  });
  if (filters.inspectionId) params.set("InspectionId", filters.inspectionId);
  if (filters.tag) params.set("Tag", filters.tag);
  if (filters.installationCode) params.set("InstallationCode", filters.installationCode);
  if (filters.anonymizationStatus) params.set("AnonymizationStatus", filters.anonymizationStatus);
  if (filters.analysisType) params.set("AnalysisType", filters.analysisType);
  if (filters.hasIncompleteWorkflows != null)
    params.set("HasIncompleteWorkflows", String(filters.hasIncompleteWorkflows));
  return apiFetch<PagedResponse<PlantData>>(apiUrl(`/api/PlantData?${params.toString()}`));
}

export async function getPlantDataById(id: string): Promise<PlantData> {
  return apiFetch<PlantData>(
    apiUrl(`/api/PlantData/id/${encodeURIComponent(id)}`)
  );
}

export async function createPlantData(
  request: PlantDataRequest
): Promise<PlantData> {
  return apiFetch<PlantData>(apiUrl("/api/PlantData"), {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export async function triggerAnonymizer(plantDataId: string): Promise<string> {
  return apiFetch<string>(
    apiUrl(`/api/TriggerAnalysis/trigger-anonymizer/${encodeURIComponent(plantDataId)}`),
    { method: "POST" }
  );
}

export async function getAnalysisMappings(
  pageNumber = 1,
  pageSize = 100
): Promise<AnalysisMapping[]> {
  return apiFetch<AnalysisMapping[]>(
    apiUrl(`/api/AnalysisMapping?PageNumber=${pageNumber}&PageSize=${pageSize}`)
  );
}

export async function createAnalysisMapping(
  tagId: string,
  inspectionDescription: string,
  analysisType: AnalysisType
): Promise<AnalysisMapping> {
  return apiFetch<AnalysisMapping>(
    apiUrl(`/api/AnalysisMapping/tag/${encodeURIComponent(tagId)}/inspection/${encodeURIComponent(inspectionDescription)}/analysisType/${encodeURIComponent(analysisType)}`),
    { method: "POST" }
  );
}

export async function deleteAnalysisMapping(id: string): Promise<void> {
  await apiFetch<void>(
    apiUrl(`/api/AnalysisMapping/analysisMappingId/${encodeURIComponent(id)}`),
    { method: "DELETE" }
  );
}
