export const parseAsDate = (value?: string | Date | null): Date | null => {
  if (!value) return null;
  if (value instanceof Date) return value;
  const str = String(value).trim();
  if (!str) return null;

  // Handle ISO strings with or without trailing timezone indicators
  const date = new Date(str);
  return isNaN(date.getTime()) ? null : date;
};

export const formatDateTime = (value?: string | Date | null) => {
  const d = parseAsDate(value);
  return d
    ? d.toLocaleString('vi-VN', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      })
    : '—';
};

export const formatDate = (value?: string | Date | null) => {
  const d = parseAsDate(value);
  return d
    ? d.toLocaleDateString('vi-VN', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
      })
    : '—';
};
