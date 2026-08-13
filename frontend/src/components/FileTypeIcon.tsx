import React from 'react';
import { FileCode2, FileSpreadsheet, FileText, FileType2, Presentation } from 'lucide-react';

const styles: Record<string, { color: string; background: string; icon: React.ElementType }> = {
  pdf: { color: '#ef4444', background: 'rgba(239,68,68,.12)', icon: FileText },
  doc: { color: '#3b82f6', background: 'rgba(59,130,246,.12)', icon: FileType2 },
  docx: { color: '#3b82f6', background: 'rgba(59,130,246,.12)', icon: FileType2 },
  xls: { color: '#10b981', background: 'rgba(16,185,129,.12)', icon: FileSpreadsheet },
  xlsx: { color: '#10b981', background: 'rgba(16,185,129,.12)', icon: FileSpreadsheet },
  csv: { color: '#10b981', background: 'rgba(16,185,129,.12)', icon: FileSpreadsheet },
  ppt: { color: '#f59e0b', background: 'rgba(245,158,11,.12)', icon: Presentation },
  pptx: { color: '#f59e0b', background: 'rgba(245,158,11,.12)', icon: Presentation },
  txt: { color: '#06b6d4', background: 'rgba(6,182,212,.12)', icon: FileCode2 },
  md: { color: '#06b6d4', background: 'rgba(6,182,212,.12)', icon: FileCode2 },
};

export const FileTypeIcon: React.FC<{ extension?: string; size?: number; className?: string }> = ({
  extension = '',
  size = 28,
  className,
}) => {
  const config = styles[extension.trim().replace(/^\./, '').toLowerCase()] ?? {
    color: '#94a3b8',
    background: 'rgba(148,163,184,.12)',
    icon: FileText,
  };
  const Icon = config.icon;
  return (
    <span
      className={className}
      style={{ color: config.color, background: config.background }}
      title={extension.toUpperCase()}
    >
      <Icon size={size} />
    </span>
  );
};
