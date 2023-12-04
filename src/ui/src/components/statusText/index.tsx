import React from 'react';
import './index.scss';

function StatusText({
  value,
  failed,
}: {
  value?: number | string;
  failed?: boolean;
}) {
  return (
    <p className={`status-text ${failed ? 'failed-status' : 'ok-status'}`}>
      {value}
    </p>
  );
}
export default StatusText;
