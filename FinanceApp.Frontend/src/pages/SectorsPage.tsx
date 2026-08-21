import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Badge,
  Button,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd';
import {
  ArrowRightOutlined,
  DeleteOutlined,
  EditOutlined,
  InboxOutlined,
  PlusOutlined,
  RollbackOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import axios from 'axios';
import AuthenticatedShell from '../components/AuthenticatedShell';
import { useAuth } from '../contexts/AuthContext';
import {
  archiveIndustry,
  archiveSector,
  createIndustry,
  createSector,
  deleteIndustry,
  deleteSector,
  getPortfolios,
  getSectors,
  moveIndustry,
  restoreIndustry,
  restoreSector,
  updateIndustry,
  updateSector,
} from '../services/api';
import type { IndustryDto, Portfolio, SectorDto } from '../types';
import {
  DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS,
  DIRECTORIES_TYPOGRAPHY_CLASS,
} from './directoriesTypography';
import './directoriesTypography.css';

const { Title, Text } = Typography;

function getErrMsg(err: unknown, fallback: string): string {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data;
    if (typeof data === 'string' && data.trim()) {
      return data.trim();
    }
    if (
      data != null
      && typeof data === 'object'
      && 'message' in data
      && typeof data.message === 'string'
      && data.message.trim()
    ) {
      return data.message.trim();
    }
  }

  return fallback;
}

const archiveTag = <Tag color="default">Архив</Tag>;

const SectorsPage: React.FC = () => {
  const { user, logout } = useAuth();
  const [portfolios, setPortfolios] = useState<Portfolio[]>([]);
  const [sectors, setSectors] = useState<SectorDto[]>([]);
  const [expandedRowKeys, setExpandedRowKeys] = useState<React.Key[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [includeArchived, setIncludeArchived] = useState(true);
  const [search, setSearch] = useState('');

  const [sectorModalOpen, setSectorModalOpen] = useState(false);
  const [sectorModalLoading, setSectorModalLoading] = useState(false);
  const [editingSector, setEditingSector] = useState<SectorDto | null>(null);
  const [sectorForm] = Form.useForm();

  const [industryModalOpen, setIndustryModalOpen] = useState(false);
  const [industryModalLoading, setIndustryModalLoading] = useState(false);
  const [editingIndustry, setEditingIndustry] = useState<IndustryDto | null>(null);
  const [editingIndustrySectorId, setEditingIndustrySectorId] = useState<number | null>(null);
  const [industryForm] = Form.useForm();

  const [moveModalOpen, setMoveModalOpen] = useState(false);
  const [moveModalLoading, setMoveModalLoading] = useState(false);
  const [movingIndustry, setMovingIndustry] = useState<IndustryDto | null>(null);
  const [moveForm] = Form.useForm();

  const [messageApi, contextHolder] = message.useMessage();

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const [sectorData, portfolioResponse] = await Promise.all([
        getSectors(includeArchived),
        getPortfolios(),
      ]);
      setSectors(sectorData);
      setPortfolios(portfolioResponse.data);
    } catch {
      setError('Ошибка загрузки данных');
    } finally {
      setLoading(false);
    }
  }, [includeArchived]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const filteredSectors = useMemo(() => {
    if (!search.trim()) {
      return sectors;
    }

    const query = search.trim().toLowerCase();
    return sectors
      .map((sector) => ({
        ...sector,
        industries: sector.industries.filter((industry) => industry.name.toLowerCase().includes(query)),
      }))
      .filter((sector) => sector.name.toLowerCase().includes(query) || sector.industries.length > 0);
  }, [search, sectors]);

  useEffect(() => {
    setExpandedRowKeys(filteredSectors.map((sector) => sector.id));
  }, [filteredSectors]);

  const openAddSector = () => {
    setEditingSector(null);
    sectorForm.resetFields();
    sectorForm.setFieldsValue({ sortOrder: (sectors.length + 1) * 10 });
    setSectorModalOpen(true);
  };

  const openEditSector = (sector: SectorDto) => {
    setEditingSector(sector);
    sectorForm.setFieldsValue({ name: sector.name, sortOrder: sector.sortOrder });
    setSectorModalOpen(true);
  };

  const handleSectorSave = async () => {
    const values = await sectorForm.validateFields();
    setSectorModalLoading(true);

    try {
      if (editingSector) {
        await updateSector(editingSector.id, { name: values.name, sortOrder: values.sortOrder ?? 0 });
        messageApi.success('Сектор обновлён');
      } else {
        await createSector({ name: values.name, sortOrder: values.sortOrder });
        messageApi.success('Сектор создан');
      }

      setSectorModalOpen(false);
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка сохранения сектора'));
    } finally {
      setSectorModalLoading(false);
    }
  };

  const handleArchiveSector = async (sector: SectorDto) => {
    try {
      await archiveSector(sector.id);
      messageApi.success('Сектор архивирован');
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка архивирования сектора'));
    }
  };

  const handleRestoreSector = async (sector: SectorDto) => {
    try {
      await restoreSector(sector.id);
      messageApi.success('Сектор восстановлен');
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка восстановления сектора'));
    }
  };

  const handleDeleteSector = async (sector: SectorDto) => {
    try {
      await deleteSector(sector.id);
      messageApi.success('Сектор удалён');
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Невозможно удалить сектор'));
    }
  };

  const openAddIndustry = (sectorId: number) => {
    setEditingIndustry(null);
    setEditingIndustrySectorId(sectorId);
    const sector = sectors.find((item) => item.id === sectorId);
    industryForm.resetFields();
    industryForm.setFieldsValue({ sortOrder: ((sector?.industries.length ?? 0) + 1) * 10 });
    setIndustryModalOpen(true);
  };

  const openEditIndustry = (industry: IndustryDto, sectorId: number) => {
    setEditingIndustry(industry);
    setEditingIndustrySectorId(sectorId);
    industryForm.setFieldsValue({ name: industry.name, sortOrder: industry.sortOrder });
    setIndustryModalOpen(true);
  };

  const handleIndustrySave = async () => {
    const values = await industryForm.validateFields();
    setIndustryModalLoading(true);

    try {
      if (editingIndustry && editingIndustrySectorId != null) {
        await updateIndustry(editingIndustrySectorId, editingIndustry.id, {
          name: values.name,
          sortOrder: values.sortOrder ?? 0,
        });
        messageApi.success('Отрасль обновлена');
      } else if (editingIndustrySectorId != null) {
        await createIndustry(editingIndustrySectorId, { name: values.name, sortOrder: values.sortOrder });
        messageApi.success('Отрасль создана');
      }

      setIndustryModalOpen(false);
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка сохранения отрасли'));
    } finally {
      setIndustryModalLoading(false);
    }
  };

  const handleArchiveIndustry = async (sectorId: number, industry: IndustryDto) => {
    try {
      await archiveIndustry(sectorId, industry.id);
      messageApi.success('Отрасль архивирована');
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка архивирования отрасли'));
    }
  };

  const handleRestoreIndustry = async (sectorId: number, industry: IndustryDto) => {
    try {
      await restoreIndustry(sectorId, industry.id);
      messageApi.success('Отрасль восстановлена');
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка восстановления отрасли'));
    }
  };

  const handleDeleteIndustry = async (sectorId: number, industry: IndustryDto) => {
    try {
      await deleteIndustry(sectorId, industry.id);
      messageApi.success('Отрасль удалена');
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Невозможно удалить отрасль'));
    }
  };

  const openMoveIndustry = (industry: IndustryDto) => {
    setMovingIndustry(industry);
    moveForm.resetFields();
    setMoveModalOpen(true);
  };

  const handleMoveIndustry = async () => {
    const values = await moveForm.validateFields();
    if (!movingIndustry) {
      return;
    }

    setMoveModalLoading(true);
    try {
      await moveIndustry(movingIndustry.sectorId, movingIndustry.id, { targetSectorId: values.targetSectorId });
      messageApi.success('Отрасль перенесена');
      setMoveModalOpen(false);
      await loadData();
    } catch (err) {
      messageApi.error(getErrMsg(err, 'Ошибка переноса отрасли'));
    } finally {
      setMoveModalLoading(false);
    }
  };

  const sectorColumns: ColumnsType<SectorDto> = [
    {
      title: 'Сектор',
      dataIndex: 'name',
      key: 'name',
      render: (_value, sector) => (
        <Space>
          <Text strong>{sector.name}</Text>
          {sector.isArchived && archiveTag}
        </Space>
      ),
    },
    {
      title: 'Отраслей',
      key: 'industryCount',
      width: 140,
      render: (_value, sector) => <Badge count={sector.industries.length} color="blue" overflowCount={999} />,
    },
    {
      title: 'Акций',
      dataIndex: 'stockCount',
      key: 'stockCount',
      width: 120,
      render: (value: number) => <Tag color="geekblue">{value}</Tag>,
    },
    {
      title: 'Действия',
      key: 'actions',
      width: 280,
      render: (_value, sector) => (
        <Space size={4}>
          <Tooltip title="Редактировать сектор" overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}>
            <Button size="small" icon={<EditOutlined />} onClick={() => openEditSector(sector)} />
          </Tooltip>
          <Tooltip title="Добавить отрасль" overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}>
            <Button size="small" icon={<PlusOutlined />} onClick={() => openAddIndustry(sector.id)} />
          </Tooltip>
          {sector.isArchived ? (
            <Tooltip title="Восстановить сектор" overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}>
              <Button size="small" icon={<RollbackOutlined />} onClick={() => void handleRestoreSector(sector)} />
            </Tooltip>
          ) : (
            <Tooltip title="Архивировать сектор" overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}>
              <Button size="small" icon={<InboxOutlined />} onClick={() => void handleArchiveSector(sector)} />
            </Tooltip>
          )}
          <Tooltip
            title={sector.industries.length > 0 ? 'Нельзя удалить: содержит отрасли' : 'Удалить сектор'}
            overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}
          >
            <span>
              <Popconfirm
                title="Удалить сектор?"
                onConfirm={() => void handleDeleteSector(sector)}
                okText="Да"
                cancelText="Нет"
                disabled={sector.industries.length > 0}
                overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}
              >
                <Button size="small" icon={<DeleteOutlined />} disabled={sector.industries.length > 0} danger />
              </Popconfirm>
            </span>
          </Tooltip>
        </Space>
      ),
    },
  ];

  const industryColumns: ColumnsType<IndustryDto> = [
    {
      title: 'Отрасль',
      dataIndex: 'name',
      key: 'name',
      render: (_value, industry) => (
        <Space>
          <Text>{industry.name}</Text>
          {industry.isArchived && archiveTag}
        </Space>
      ),
    },
    {
      title: 'Акций',
      dataIndex: 'stockCount',
      key: 'stockCount',
      width: 120,
      render: (value: number) => <Tag color="geekblue">{value}</Tag>,
    },
    {
      title: 'Действия',
      key: 'actions',
      width: 260,
      render: (_value, industry) => (
        <Space size={4}>
          <Tooltip title="Редактировать" overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}>
            <Button
              size="small"
              icon={<EditOutlined />}
              onClick={() => openEditIndustry(industry, industry.sectorId)}
            />
          </Tooltip>
          <Tooltip title="Перенести в другой сектор" overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}>
            <Button size="small" icon={<ArrowRightOutlined />} onClick={() => openMoveIndustry(industry)} />
          </Tooltip>
          {industry.isArchived ? (
            <Tooltip title="Восстановить" overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}>
              <Button
                size="small"
                icon={<RollbackOutlined />}
                onClick={() => void handleRestoreIndustry(industry.sectorId, industry)}
              />
            </Tooltip>
          ) : (
            <Tooltip title="Архивировать" overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}>
              <Button
                size="small"
                icon={<InboxOutlined />}
                onClick={() => void handleArchiveIndustry(industry.sectorId, industry)}
              />
            </Tooltip>
          )}
          <Tooltip
            title={industry.stockCount > 0 ? `Нельзя удалить: используется в ${industry.stockCount} акциях` : 'Удалить'}
            overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}
          >
            <span>
              <Popconfirm
                title="Удалить отрасль?"
                onConfirm={() => void handleDeleteIndustry(industry.sectorId, industry)}
                okText="Да"
                cancelText="Нет"
                disabled={industry.stockCount > 0}
                overlayClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}
              >
                <Button size="small" icon={<DeleteOutlined />} disabled={industry.stockCount > 0} danger />
              </Popconfirm>
            </span>
          </Tooltip>
        </Space>
      ),
    },
  ];

  return (
    <>
      {contextHolder}
      <AuthenticatedShell
        portfolios={portfolios}
        selectedKeys={['sectors']}
        onLogout={logout}
        userName={user?.username}
        activePortfolioId={undefined}
        headerLeft={<Title level={4} style={{ margin: 0 }}>Секторы и отрасли</Title>}
        headerRight={(
          <div className={DIRECTORIES_TYPOGRAPHY_CLASS}>
            <Space>
              <Button onClick={() => setIncludeArchived((value) => !value)} type={includeArchived ? 'default' : 'dashed'}>
                {includeArchived ? 'Скрыть архивные' : 'Показать архивные'}
              </Button>
              <Button type="primary" icon={<PlusOutlined />} onClick={openAddSector}>
                Добавить сектор
              </Button>
            </Space>
          </div>
        )}
      >
        <div className={DIRECTORIES_TYPOGRAPHY_CLASS} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
          <Input.Search
            placeholder="Поиск по сектору или отрасли..."
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            allowClear
            style={{ maxWidth: 420 }}
          />

          {loading ? (
            <div style={{ textAlign: 'center', padding: 48 }}>
              <Spin size="large" />
            </div>
          ) : error ? (
            <Text type="danger">{error}</Text>
          ) : filteredSectors.length === 0 ? (
            <Text type="secondary">Нет данных</Text>
          ) : (
            <Table<SectorDto>
              rowKey="id"
              columns={sectorColumns}
              dataSource={filteredSectors}
              pagination={false}
              expandable={{
                expandedRowKeys,
                onExpandedRowsChange: (keys) => setExpandedRowKeys([...keys]),
                expandedRowRender: (sector) => (
                  sector.industries.length === 0 ? (
                    <Text type="secondary">Нет отраслей</Text>
                  ) : (
                    <Table<IndustryDto>
                      rowKey="id"
                      columns={industryColumns}
                      dataSource={sector.industries}
                      pagination={false}
                      size="small"
                    />
                  )
                ),
              }}
            />
          )}
        </div>
      </AuthenticatedShell>

      <Modal
        title={editingSector ? 'Редактировать сектор' : 'Добавить сектор'}
        open={sectorModalOpen}
        onOk={() => void handleSectorSave()}
        onCancel={() => setSectorModalOpen(false)}
        confirmLoading={sectorModalLoading}
        okText="Сохранить"
        cancelText="Отмена"
        rootClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}
      >
        <Form form={sectorForm} layout="vertical">
          <Form.Item
            name="name"
            label="Название"
            rules={[{ required: true, message: 'Введите название' }, { max: 200 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="sortOrder" label="Порядок сортировки">
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={editingIndustry ? 'Редактировать отрасль' : 'Добавить отрасль'}
        open={industryModalOpen}
        onOk={() => void handleIndustrySave()}
        onCancel={() => setIndustryModalOpen(false)}
        confirmLoading={industryModalLoading}
        okText="Сохранить"
        cancelText="Отмена"
        rootClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}
      >
        <Form form={industryForm} layout="vertical">
          <Form.Item
            name="name"
            label="Название"
            rules={[{ required: true, message: 'Введите название' }, { max: 200 }]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="sortOrder" label="Порядок сортировки">
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="Перенести отрасль"
        open={moveModalOpen}
        onOk={() => void handleMoveIndustry()}
        onCancel={() => setMoveModalOpen(false)}
        confirmLoading={moveModalLoading}
        okText="Перенести"
        cancelText="Отмена"
        rootClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}
      >
        <Form form={moveForm} layout="vertical">
          <Form.Item
            name="targetSectorId"
            label="Целевой сектор"
            rules={[{ required: true, message: 'Выберите сектор' }]}
          >
            <Select
              options={sectors
                .filter((sector) => !sector.isArchived && sector.id !== movingIndustry?.sectorId)
                .map((sector) => ({
                  value: sector.id,
                  label: sector.name,
                }))}
              placeholder="Выберите сектор"
              popupClassName={DIRECTORIES_OVERLAY_TYPOGRAPHY_CLASS}
            />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default SectorsPage;
