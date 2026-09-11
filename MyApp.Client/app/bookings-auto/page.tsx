'use client'

import { useState } from "react"
import { useFormatters, AutoQueryGrid, TextLink, PreviewFormat, Icon } from "@servicestack/react"
import Page from "@/components/layout-page"
import { ValidateAuth } from "@/lib/auth"

function BookingsCustom() {
    const { currency } = useFormatters()
    const [coupon, setCoupon] = useState<any>(null)

    return (<Page title="AutoQueryGrid Bookings CRUD Example">

        <div className="mt-4 flex flex-col ">
            <AutoQueryGrid type="Booking"/>

            <h3 className="text-4xl font-bold my-8 text-gray-900 dark:text-gray-100">
                Custom AutoQueryGrid Example
            </h3>

            <AutoQueryGrid
                type="Booking"
                hide={["downloadCsv","copyApiUrl"]}
                selectedColumns={['id', 'name', 'cost', 'bookingStartDate', 'bookingEndDate', 'roomNumber', 'createdBy', 'discount']}
                visibleFrom={{
                    name: 'xl',
                    bookingStartDate: 'sm',
                    bookingEndDate: 'xl',
                    createdBy: '2xl'
                }}
                columnSlots={{
                    id: ({ id }: any) => (
                        <span className="text-gray-900" dangerouslySetInnerHTML={{ __html: id }} />
                    ),
                    name: ({ name }: any) => <>{name}</>,
                    cost: ({ cost }: any) => (
                        <span dangerouslySetInnerHTML={{ __html: currency(cost) }} />
                    ),
                    createdBy: ({ createdBy }: any) => (
                        <span dangerouslySetInnerHTML={{ __html: createdBy }} />
                    ),
                    discount: ({ discount }: any) => (
                        discount ? (
                            <TextLink
                                className="flex items-end"
                                onClick={(e: React.MouseEvent) => {
                                    e.stopPropagation()
                                    setCoupon(discount)
                                }}
                                title={discount.id}
                            >
                                <Icon className="w-5 h-5 mr-1" type="Coupon" />
                                <PreviewFormat value={discount.description} />
                            </TextLink>
                        ) : null
                    )
                }}
                headerSlots={{
                    'roomNumber-header': () => (
                        <><span className="hidden lg:inline">Room </span>No</>
                    ),
                    'bookingStartDate-header': () => (
                        <>Start<span className="hidden lg:inline"> Date</span></>
                    ),
                    'bookingEndDate-header': () => (
                        <>End<span className="hidden lg:inline"> Date</span></>
                    ),
                    'createdBy-header': () => <>Employee</>
                }}
            />
        </div>

    </Page>)
}

export default ValidateAuth(BookingsCustom, {role: 'Employee'})
