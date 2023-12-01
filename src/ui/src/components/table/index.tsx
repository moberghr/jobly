import { useState } from 'react';
import TableComponent from 'react-bootstrap/Table';
import Pagination from 'react-bootstrap/Pagination';
import Dropdown from 'react-bootstrap/Dropdown';
import DropdownButton from 'react-bootstrap/DropdownButton';

import './index.scss';

const ITEMS_PER_PAGE_OPTIONS = [10, 20, 50, 100, 200, 500];

function Table() {
  const [itemsPerPage, setItemsPerPage] = useState(10);

  return (
    <>
      <TableComponent hover className='table'>
        <thead>
          <tr>
            <th>#</th>
            <th>First Name</th>
            <th>Last Name</th>
            <th>Username</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>1</td>
            <td>Mark</td>
            <td>Otto</td>
            <td>@mdo</td>
          </tr>
          <tr>
            <td>2</td>
            <td>Jacob</td>
            <td>Thornton</td>
            <td>@fat</td>
          </tr>
          <tr>
            <td>3</td>
            <td colSpan={2}>Larry the Bird</td>
            <td>@twitter</td>
          </tr>
        </tbody>
      </TableComponent>

      <div className='table-footer'>
        <p>Selected 0 of 174991</p>
        <div className='items-per-page'>
          <p>Items per page </p>
          <DropdownButton
            id='dropdown-basic-button'
            title={itemsPerPage}
            size='sm'
            className='small-dropdown'
          >
            {ITEMS_PER_PAGE_OPTIONS.map((num) => (
              <Dropdown.Item key={num} onClick={() => setItemsPerPage(num)}>
                {num}
              </Dropdown.Item>
            ))}
          </DropdownButton>
        </div>

        <p>
          1-25 of <b>174991</b>
        </p>
        <Pagination>
          <Pagination.First />
          <Pagination.Prev />
          <Pagination.Item>{1}</Pagination.Item>
          <Pagination.Item>{2}</Pagination.Item>
          <Pagination.Item>{3}</Pagination.Item>
          <Pagination.Ellipsis />
          <Pagination.Item>{20}</Pagination.Item>
          <Pagination.Next />
          <Pagination.Last />
        </Pagination>
      </div>
    </>
  );
}

export default Table;
