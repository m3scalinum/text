// PDF — every item of the category, invoked once.
#pdf.attach("data.txt", description: "An attachment")

#pdf.artifact[An artifact]

#table(
  columns: 2,
  pdf.header-cell[Header],
  pdf.header-cell[Value],
  pdf.data-cell[Row],
  pdf.data-cell[1],
)

#pdf.table-summary(summary: "A summary of the table.", table(columns: 1, [cell]))
