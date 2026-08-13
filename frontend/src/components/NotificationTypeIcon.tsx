import React from 'react';
import {
  AlertTriangle,
  Bot,
  FileCheck2,
  FileSearch,
  HandCoins,
  MessageSquareReply,
  ReceiptText,
  UserPlus,
} from 'lucide-react';

export const NotificationTypeIcon: React.FC<{ type?: string; size?: number }> = ({
  type = '',
  size = 18,
}) => {
  const normalized = type.toUpperCase();
  const Icon =
    normalized === 'FRIEND_REQUEST'
      ? UserPlus
      : normalized === 'TRANSACTION_PENDING'
        ? HandCoins
        : normalized === 'TRANSACTION_RESOLVED'
          ? ReceiptText
          : normalized === 'DOCUMENT_AI_READY'
            ? Bot
            : normalized === 'DOCUMENT_REVIEW_PENDING'
              ? FileCheck2
              : normalized === 'REPORT_PENDING'
                ? FileSearch
                : normalized === 'APPEAL_PENDING'
                  ? MessageSquareReply
                  : AlertTriangle;
  return <Icon size={size} aria-hidden="true" />;
};
