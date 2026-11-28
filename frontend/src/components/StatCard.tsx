import { ReactNode } from "react";
import { Icon, Tooltip, Typography } from "@equinor/eds-core-react";
import { help_outline } from "@equinor/eds-icons";
import { tokens } from "@equinor/eds-tokens";
import styled from "styled-components";
import { statusColors, Surface } from "./Styles";

type Tone = keyof typeof statusColors;

const Card = styled(Surface)`
  padding: 0.5rem 0.75rem;
  min-width: 120px;
  flex: 1 1 120px;
`;

const Header = styled.div`
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 0.25rem;
`;

const HelpTrigger = styled.button`
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  padding: 0;
  border: 0;
  background: transparent;
  color: ${tokens.colors.text.static_icons__tertiary.hex};
  cursor: help;
  border-radius: 50%;
  line-height: 1;

  &:hover,
  &:focus-visible {
    color: ${tokens.colors.text.static_icons__default.hex};
  }

  &:focus-visible {
    outline: 2px solid ${tokens.colors.interactive.focus.hex};
    outline-offset: 2px;
  }
`;

const Value = styled(Typography)`
  font-size: 1.4rem;
  font-weight: 600;
  line-height: 1.15;
`;

const TOOLTIP_STYLE = {
  whiteSpace: "normal" as const,
  maxWidth: "260px",
  lineHeight: 1.4,
};

interface Props {
  title: string;
  value: ReactNode;
  tone?: Tone;
  subtitle?: ReactNode;
  tooltip?: string;
}

export default function StatCard({
  title,
  value,
  tone = "default",
  subtitle,
  tooltip,
}: Props) {
  const { accent, text } = statusColors[tone];
  return (
    <Card $accent={accent}>
      <Header>
        <Typography
          variant="caption"
          style={{
            color: tokens.colors.text.static_icons__tertiary.hex,
            textTransform: "uppercase",
            letterSpacing: "0.04em",
          }}
        >
          {title}
        </Typography>
        {tooltip && (
          <Tooltip title={tooltip} placement="top" style={TOOLTIP_STYLE}>
            <HelpTrigger type="button" aria-label={`Explain ${title}`}>
              <Icon data={help_outline} size={16} />
            </HelpTrigger>
          </Tooltip>
        )}
      </Header>
      <Value variant="h4" style={{ color: text }}>
        {value}
      </Value>
      {subtitle && (
        <Typography
          variant="caption"
          style={{
            color: tokens.colors.text.static_icons__tertiary.hex,
            fontSize: "0.7rem",
          }}
        >
          {subtitle}
        </Typography>
      )}
    </Card>
  );
}
