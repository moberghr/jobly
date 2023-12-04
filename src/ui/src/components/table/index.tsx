import { useEffect, useState, ComponentType, ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import TableComponent from 'react-bootstrap/Table';
import Pagination from 'react-bootstrap/Pagination';
import Dropdown from 'react-bootstrap/Dropdown';
import DropdownButton from 'react-bootstrap/DropdownButton';

import './index.scss';

const ITEMS_PER_PAGE_OPTIONS = [10, 20, 50, 100, 200, 500];

type AdditionalProps = {
  additionalProp: any;
};

type WrapperComponentProps = {
  WrappedComponent: ComponentType<any>;
  children?: ReactNode;
} & AdditionalProps;

const WrapperComponent: React.FC<WrapperComponentProps> = ({
  WrappedComponent,
  additionalProp,
  children,
}) => {
  return <WrappedComponent {...additionalProp}>{children}</WrappedComponent>;
};

type ITableProps = {
  data: {
    data: {
      [key: string]: any;
    }[];
    totalCount: number;
  };
  columnNames: {
    [key: string]: string;
  };
  specialColumns?: string[];
  specialColumnComponents?: {
    [key: string]: any;
  };
};

const Table = ({
  data,
  columnNames,
  specialColumns,
  specialColumnComponents,
}: ITableProps) => {
  let [searchParams, setSearchParams] = useSearchParams();
  const [itemsPerPage, setItemsPerPage] = useState(10);
  const [currentPage, setCurrentPage] = useState(1);

  const maxPage = Math.ceil(data.totalCount / itemsPerPage);

  const deleteSearchParams = () => {
    searchParams.delete('page');
    searchParams.delete('items');
    setSearchParams(searchParams);
  };

  const handlePaginationChange = (page: number) => {
    setCurrentPage(page);
    deleteSearchParams();
  };

  const handleItemsNumChange = (items: number) => {
    setItemsPerPage(items);
    deleteSearchParams();
  };

  useEffect(() => {
    setSearchParams((params) => {
      if (!params.get('page')) params.set('page', currentPage.toString());
      if (!params.get('items')) params.set('items', itemsPerPage.toString());

      if (
        params.get('items') &&
        params.get('items') !== itemsPerPage.toString()
      )
        setItemsPerPage(Number(params.get('items')));

      if (params.get('page') && params.get('page') !== currentPage.toString())
        setCurrentPage(Number(params.get('page')));

      return params;
    });
  }, [currentPage, itemsPerPage, setSearchParams]);

  return (
    <>
      <TableComponent hover responsive className='table'>
        <thead>
          <tr>
            {Object.values(columnNames).map((name) => (
              <th key={name}>{name}</th>
            ))}
          </tr>
        </thead>
        {data.data.length > 0 && (
          <tbody>
            {data.data.map((row, index) => (
              <tr
                key={
                  row.id &&
                  (typeof row.id === 'string' || typeof row.id === 'number')
                    ? row.id
                    : index
                }
              >
                {Object.keys(columnNames).map((name) =>
                  specialColumns &&
                  specialColumns.includes(name) &&
                  specialColumnComponents &&
                  specialColumnComponents[name] &&
                  typeof row[name] === 'object' ? (
                    <td key={row[name].value}>
                      <WrapperComponent
                        WrappedComponent={specialColumnComponents.lastExecution}
                        additionalProp={{ ...row[name] }}
                      />
                    </td>
                  ) : (
                    <td key={row[name]}>{row[name]}</td>
                  )
                )}
              </tr>
            ))}
          </tbody>
        )}
      </TableComponent>

      <div className='table-footer'>
        {data.data.length > 0 && (
          <>
            <p className='num-of-items'>Selected 0 of {data.totalCount}</p>
            <div className='items-per-page'>
              <p>Items per page </p>
              <DropdownButton
                id='dropdown-basic-button'
                title={itemsPerPage}
                size='sm'
                className='small-dropdown'
              >
                {ITEMS_PER_PAGE_OPTIONS.map((num) => (
                  <Dropdown.Item
                    key={num}
                    onClick={() => handleItemsNumChange(num)}
                  >
                    {num}
                  </Dropdown.Item>
                ))}
              </DropdownButton>
            </div>

            <p>
              {itemsPerPage * (currentPage - 1)}-
              {itemsPerPage * (currentPage - 1) + data.data.length} of{' '}
              <b>{data.totalCount}</b>
            </p>

            <Pagination size='sm'>
              <Pagination.First
                disabled={currentPage === 1}
                onClick={() => handlePaginationChange(1)}
              />
              <Pagination.Prev
                disabled={currentPage === 1}
                onClick={() => handlePaginationChange(currentPage - 1)}
              />
              <Pagination.Item
                active={currentPage === 1}
                onClick={() => handlePaginationChange(1)}
              >
                {1}
              </Pagination.Item>
              {currentPage > 2 && <Pagination.Ellipsis />}
              {currentPage !== 1 && currentPage !== maxPage && (
                <Pagination.Item active={true}>{currentPage}</Pagination.Item>
              )}
              {currentPage < maxPage - 1 && <Pagination.Ellipsis />}
              {maxPage !== 1 && (
                <Pagination.Item
                  active={currentPage === maxPage}
                  onClick={() => handlePaginationChange(maxPage)}
                >
                  {maxPage}
                </Pagination.Item>
              )}
              <Pagination.Next
                disabled={maxPage === currentPage}
                onClick={() => handlePaginationChange(currentPage + 1)}
              />
              <Pagination.Last
                disabled={maxPage === currentPage}
                onClick={() => handlePaginationChange(maxPage)}
              />
            </Pagination>
          </>
        )}
        {!data.data ||
          (data.data.length === 0 && (
            <p className='no-data'>There is no data.</p>
          ))}
      </div>
    </>
  );
};

export default Table;
