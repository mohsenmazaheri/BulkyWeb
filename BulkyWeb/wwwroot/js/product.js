var dataTable;

$(document).ready(function () {
    loadDataTable();
});

function loadDataTable() {
    dataTable = $('#tblData').DataTable({
        "ajax": { url: '/admin/product/getall' },
        "columns": [
            { data: 'title', "width" : "25%" },
            { data: 'isbn', "width": "15%" },
            { data: 'listPrice', "width": "10%" },
            { data: 'author', "width": "15%" },
            { data: 'category.name', "width": "10%" },
            {
                data: 'id',
                render: function (data) {
                    return `
                    <div class="btn-group w-75" role="group">
                        <a href="/admin/product/upsert?id=${data}" 
                           class="btn btn-primary mx-1 d-flex align-items-center">
                            <i class="bi bi-pencil-square me-1"></i> Edit
                        </a>

                        <a href="javascript:void(0)" 
                           onclick="Delete('/admin/product/delete/${data}')" 
                           class="btn btn-danger mx-1 d-flex align-items-center">
                            <i class="bi bi-trash-fill me-1"></i> Delete
                        </a>
                    </div>`;
                },
                width: "25%"
            }
        ]
    });
}

function Delete(url){
    Swal.fire({
        title: "Are you sure?",
        text: "You won't be able to revert this!",
        icon: "warning",
        showCancelButton: true,
        confirmButtonColor: "#3085d6",
        cancelButtonColor: "#d33",
        confirmButtonText: "Yes, delete it!"
    }).then((result) => {
        if (result.isConfirmed) {
            $.ajax({
                url: url,
                type: 'DELETE',
                success: function (data) {
                    dataTable.ajax.reload();
                    toastr.success(data.message);
                }
            })
            //Swal.fire({
            //    title: "Deleted!",
            //    text: "Your file has been deleted.",
            //    icon: "success"
            //});
        }
    })
}