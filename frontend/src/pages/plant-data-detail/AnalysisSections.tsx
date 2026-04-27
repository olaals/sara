import { getPlantDataWorkflowStep } from "../../api/client";
import type { PlantData } from "../../api/client";
import WorkflowSection from "./WorkflowSection";

export default function AnalysisSections({ data }: { data: PlantData }) {
  const cloeStep = getPlantDataWorkflowStep(data, "CLOEAnalysis");
  const fencillaStep = getPlantDataWorkflowStep(data, "FencillaAnalysis");
  const thermalReadingStep = getPlantDataWorkflowStep(data, "ThermalReadingAnalysis");

  return (
    <>
      <WorkflowSection
        title="CLOE Analysis"
        workflow={cloeStep}
        extraFields={
          cloeStep
            ? [
                { label: "Oil Level", value: cloeStep.cloeData?.oilLevel?.toString() ?? "-" },
                { label: "Confidence", value: cloeStep.cloeData?.confidence?.toString() ?? "-" },
              ]
            : undefined
        }
      />

      <WorkflowSection
        title="Fencilla Analysis"
        workflow={fencillaStep}
        extraFields={
          fencillaStep
            ? [
                {
                  label: "Is Break",
                  value: fencillaStep.fencillaData?.isBreak == null
                    ? "-"
                    : fencillaStep.fencillaData.isBreak
                      ? "Yes"
                      : "No",
                },
                { label: "Confidence", value: fencillaStep.fencillaData?.confidence?.toString() ?? "-" },
              ]
            : undefined
        }
      />

      <WorkflowSection
        title="Thermal Reading Analysis"
        workflow={thermalReadingStep}
        extraFields={
          thermalReadingStep
            ? [
                { label: "Temperature", value: thermalReadingStep.thermalReadingData?.temperature?.toString() ?? "-" },
              ]
            : undefined
        }
      />
    </>
  );
}
