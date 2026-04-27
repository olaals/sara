import { Typography } from "@equinor/eds-core-react";
import { getPlantDataWorkflowStep } from "../../api/client";
import type { PlantData } from "../../api/client";
import WorkflowTriggerButton from "../../components/WorkflowTriggerButton";
import WorkflowSection from "./WorkflowSection";
import styled from "styled-components";

const StyledSectionHeader = styled.div`
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
`;

interface AnonymizationSectionProps {
  data: PlantData;
  triggering: boolean;
  onTrigger: (plantData: PlantData) => Promise<void> | void;
}

export default function AnonymizationSection({ data, triggering, onTrigger }: AnonymizationSectionProps) {
  const anonymizationStep = getPlantDataWorkflowStep(data, "Anonymization");

  return (
    <>
      <StyledSectionHeader>
        <Typography variant="h5">Anonymization</Typography>
        <WorkflowTriggerButton data={data} triggering={triggering} onTrigger={onTrigger} />
      </StyledSectionHeader>
      <WorkflowSection
        title=""
        workflow={anonymizationStep}
        extraFields={
          anonymizationStep
            ? [
                {
                  label: "Person in Image",
                  value: anonymizationStep.anonymizationData?.isPersonInImage == null
                    ? "-"
                    : anonymizationStep.anonymizationData.isPersonInImage
                      ? "Yes"
                      : "No",
                },
                ...(anonymizationStep.anonymizationData?.preProcessedBlobStorageLocation
                  ? [{
                      label: "Pre-processed Location",
                      value: `${anonymizationStep.anonymizationData.preProcessedBlobStorageLocation.storageAccount}/${anonymizationStep.anonymizationData.preProcessedBlobStorageLocation.blobContainer}/${anonymizationStep.anonymizationData.preProcessedBlobStorageLocation.blobName}`,
                    }]
                  : []),
              ]
            : undefined
        }
      />
    </>
  );
}
