import React from 'react';
import './index.scss';

function Title({ children }: { children: React.ReactNode }) {
  return (
    <>
      <h1 className='title'>{children}</h1>
      <div className='line' />
    </>
  );
}

export default Title;
